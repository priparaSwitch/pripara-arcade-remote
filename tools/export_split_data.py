"""Export the coordinate remote's editable SQLite/PNG-ZIP data companions.

Original catalog.json/thumbs.zip are read-only; no EXE, game, save, launcher or
deployment files are touched. Image optimization uses optional Pillow. Use
--original-images for an exact PNG-byte copy with Python's standard library.
The .bin suffix deliberately contains a normal ZIP archive.
"""
from __future__ import annotations
import argparse
import collections
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import tempfile
import time
import zipfile

APPLICATION_ID = 0x50435231  # PCR1
SCHEMA_VERSION = 1
ITEM_FIELDS = ('id','name','jpName','brand','brandName','category','model',
               'isDress','sourceRow','thumbnail','search','nameSource')
SET_FIELDS = ('id','name','brand','brandName','search','top','bottom','shoes','head')
SLOTS = ('top','bottom','shoes','head')
CATEGORIES = frozenset(SLOTS)
PNG_SIGNATURE = b'\x89PNG\r\n\x1a\n'
IMAGE_NAME = re.compile(r'(0|[1-9][0-9]*)\.png\Z')
DATABASE_FILE = 'data.db'
IMAGES_FILE = 'images.bin'

def json_text(value):
    return json.dumps(value, ensure_ascii=False, separators=(',',':'))

def sha256(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda:f.read(1024*1024), b''):h.update(block)
    return h.hexdigest()

def integer(value, label, minimum=0):
    if isinstance(value,bool) or not isinstance(value,int) or not minimum <= value <= 0x7FFFFFFF:
        raise ValueError(f'{label}: expected Int32 integer between {minimum} and 2147483647')
    return value

def text(value,label):
    if not isinstance(value,str):raise ValueError(f'{label}: expected text')
    if '\0' in value:raise ValueError(f'{label}: NUL characters are not allowed')
    try:value.encode('utf8')
    except UnicodeEncodeError as exc:raise ValueError(f'{label}: invalid Unicode text')from exc
    return value

def required_text(value,label):
    value=text(value,label)
    if not value.strip():raise ValueError(f'{label}: expected nonblank text')
    return value

def flag(value,label):
    if not isinstance(value,bool):raise ValueError(f'{label}: expected JSON boolean')
    return int(value)

def read_catalog(path):
    with Path(path).open('r',encoding='utf-8-sig') as f:catalog=json.load(f)
    if not isinstance(catalog,dict) or not isinstance(catalog.get('items'),list) or not isinstance(catalog.get('sets'),list):
        raise ValueError('catalog must contain items and sets arrays')
    items,sets,ids,set_ids=[],[],set(),set()
    item_categories={};dress_ids=set()
    item_extras,set_extras={},{}
    for index,row in enumerate(catalog['items']):
        if not isinstance(row,dict):raise ValueError(f'items[{index}]: expected object')
        ident=integer(row.get('id'),f'items[{index}].id',1)
        if ident in ids:raise ValueError(f'duplicate item ID {ident}')
        ids.add(ident)
        values={key:row.get(key)for key in ITEM_FIELDS}
        for key in ('name','jpName','brandName','category','model','search'):
            values[key]=text(values[key],f'item {ident}.{key}')
        required_text(values['name'],f'item {ident}.name')
        if values['category'] not in CATEGORIES:raise ValueError(f'item {ident}: unsupported category {values["category"]!r}')
        values['brand']=integer(values['brand'],f'item {ident}.brand')
        values['sourceRow']=integer(values['sourceRow'],f'item {ident}.sourceRow')
        values['isDress']=flag(values['isDress'],f'item {ident}.isDress')
        if values['isDress'] and values['category']!='top':raise ValueError(f'item {ident}: a dress must use category top')
        values['thumbnail']=flag(values['thumbnail'],f'item {ident}.thumbnail')
        if values['nameSource'] is not None:values['nameSource']=text(values['nameSource'],f'item {ident}.nameSource')
        items.append(tuple(values[key]for key in ITEM_FIELDS))
        item_categories[ident]=values['category']
        if values['isDress']:dress_ids.add(ident)
        extra={key:value for key,value in row.items()if key not in ITEM_FIELDS}
        if extra:item_extras[str(ident)]=extra
    if not items:raise ValueError('catalog item list must not be empty')
    for index,row in enumerate(catalog['sets']):
        if not isinstance(row,dict):raise ValueError(f'sets[{index}]: expected object')
        ident=required_text(row.get('id'),f'sets[{index}].id')
        if ident in set_ids:raise ValueError(f'duplicate set ID {ident!r}')
        set_ids.add(ident)
        pieces=row.get('pieces')
        if not isinstance(pieces,dict):raise ValueError(f'set {ident}: expected pieces object')
        values={key:row.get(key)for key in SET_FIELDS if key not in SLOTS}
        for key in ('name','brandName','search'):values[key]=text(values[key],f'set {ident}.{key}')
        required_text(values['name'],f'set {ident}.name')
        values['brand']=integer(values['brand'],f'set {ident}.brand')
        for slot in SLOTS:
            values[slot]=integer(pieces.get(slot),f'set {ident}.{slot}')
            if values[slot] and values[slot] not in ids:
                raise ValueError(f'set {ident}.{slot}: missing item ID {values[slot]}')
            if values[slot] and item_categories[values[slot]]!=slot:
                raise ValueError(f'set {ident}.{slot}: item {values[slot]} uses category {item_categories[values[slot]]}')
        if not any(values[slot]for slot in SLOTS):raise ValueError(f'set {ident}: expected at least one piece')
        if values['top'] in dress_ids and values['bottom']:
            raise ValueError(f'set {ident}: a dress cannot be combined with a bottom')
        sets.append(tuple(values[key]for key in SET_FIELDS))
        extra={key:value for key,value in row.items()if key not in ('id','name','brand','brandName','search','pieces')}
        piece_extra={key:value for key,value in pieces.items()if key not in SLOTS}
        if piece_extra:extra['pieces']=piece_extra
        if extra:set_extras[ident]=extra
    return catalog,items,sets,item_extras,set_extras

def inspect_images(path,ids,declared):
    entries=[]
    with zipfile.ZipFile(path,'r') as z:
        names=set();image_ids=set()
        for info in z.infolist():
            match=IMAGE_NAME.fullmatch(info.filename)
            if not match or info.is_dir():raise ValueError(f'non-canonical image entry {info.filename!r}; expected ID.png')
            ident=int(match.group(1))
            if info.filename in names or ident in image_ids:raise ValueError(f'duplicate image entry {info.filename}')
            if ident not in ids:raise ValueError(f'image {info.filename} has no catalog item')
            if info.flag_bits & 1:raise ValueError(f'encrypted image entry {info.filename}')
            names.add(info.filename);image_ids.add(ident);entries.append(info)
        if image_ids != declared:
            raise ValueError(f'thumbnail flags and ZIP differ: missing={sorted(declared-image_ids)[:10]}, unexpected={sorted(image_ids-declared)[:10]}')
    return entries

def image_pipeline(max_side,colors,quantizer='adaptive'):
    if max_side is not None and (isinstance(max_side,bool) or not isinstance(max_side,int) or not 1<=max_side<=4096):
        raise ValueError('image_max_side must be None or an integer from1 to4096')
    if colors is not None and (isinstance(colors,bool) or not isinstance(colors,int) or not 1<=colors<=256):
        raise ValueError('image_colors must be None or an integer from1 to256')
    if quantizer not in ('adaptive','fast','median'):raise ValueError('image_quantizer must be adaptive, fast or median')
    return dict(max_side=max_side,colors=colors,never_upscale=True,resampling='LANCZOS'if max_side else None,
                quantization=quantizer if colors else None,dither='NONE'if colors else None,
                adaptive_white_collapse=dict(source_min_at_least=160,source_min_below=242,source_max_at_least=200,
                    source_chroma_at_most=50,quantized_min_at_least=250,foreground_min_below=250,
                    minimum_collapsed_pixels=32,minimum_foreground_percent=6.5,
                    primary='RGBA FASTOCTREE',fallback='RGB MEDIANCUT')if colors and quantizer=='adaptive'else None,
                transparent_policy='Retain resized RGBA pixels without palette quantization whenever alpha is not fully opaque',
                png_compress_level=9 if max_side or colors else None)

def process_png(raw,pipeline):
    try:from PIL import Image,ImageChops
    except ImportError as exc:raise ValueError('Image optimization requires Pillow; use a Python environment with Pillow or --original-images')from exc
    import io
    with Image.open(io.BytesIO(raw))as source:
        source.load();original_size=source.size;image=source.convert('RGBA')
    original_alpha=image.getchannel('A').getextrema()
    if pipeline['max_side']:image.thumbnail((pipeline['max_side'],pipeline['max_side']),Image.Resampling.LANCZOS)
    alpha=image.getchannel('A').getextrema();transparent=alpha!=(255,255)
    quantizer=None;collapse_pixels=0;foreground_pixels=0
    if pipeline['colors'] and not transparent:
        if pipeline['quantization']=='median':
            image=image.convert('RGB').quantize(colors=pipeline['colors'],method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
            quantizer='RGB MEDIANCUT'
        else:
            reference=image.convert('RGB')
            fast=image.quantize(colors=pipeline['colors'],method=Image.Quantize.FASTOCTREE,dither=Image.Dither.NONE)
            image=fast;quantizer='RGBA FASTOCTREE'
            if pipeline['quantization']=='adaptive':
                r,g,b=reference.split();lo=ImageChops.darker(ImageChops.darker(r,g),b);hi=ImageChops.lighter(ImageChops.lighter(r,g),b)
                foreground_pixels=sum(lo.histogram()[:250])
                pale=lo.point([255 if 160<=v<242 else 0 for v in range(256)])
                pale=ImageChops.multiply(pale,hi.point([255 if v>=200 else 0 for v in range(256)]))
                chroma=ImageChops.subtract(hi,lo)
                pale=ImageChops.multiply(pale,chroma.point([255 if v<=50 else 0 for v in range(256)]))
                r,g,b=fast.convert('RGBA').convert('RGB').split()
                fast_lo=ImageChops.darker(ImageChops.darker(r,g),b)
                collapsed=ImageChops.multiply(pale,fast_lo.point([255 if v>=250 else 0 for v in range(256)]))
                collapse_pixels=collapsed.histogram()[255]
                if collapse_pixels>=32 and collapse_pixels*1000>=foreground_pixels*65:
                    image=reference.quantize(colors=pipeline['colors'],method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
                    quantizer='RGB MEDIANCUT'
    output=io.BytesIO();image.save(output,format='PNG',optimize=True,compress_level=9)
    rgba=image.convert('RGBA')
    return output.getvalue(),dict(source_size=original_size,output_size=image.size,
                source_alpha='opaque'if original_alpha==(255,255)else'transparent',
                indexed=image.mode=='P',transparent_RGBA_preserved=transparent,
                quantizer=quantizer,white_collapse_pixels=collapse_pixels,foreground_pixels=foreground_pixels,
                rgba_sha256=hashlib.sha256(rgba.tobytes()).hexdigest())

def build_images(source,dest,entries,compression,*,max_side=None,colors=None,quantizer='adaptive'):
    pipeline=image_pipeline(max_side,colors,quantizer);transform=bool(max_side or colors)
    compression_type=zipfile.ZIP_STORED if compression=='store'else zipfile.ZIP_DEFLATED
    hashes={};pixel_hashes={};plain_bytes=0;source_bytes=0
    source_sizes=collections.Counter();output_sizes=collections.Counter();alpha=collections.Counter()
    indexed_count=0;transparent_count=0;quantizers=collections.Counter()
    with zipfile.ZipFile(source,'r')as zin,zipfile.ZipFile(dest,'w',compression=compression_type,compresslevel=6 if compression_type else None,allowZip64=True)as zout:
        for original in entries:
            raw=zin.read(original)  # This also verifies the original ZIP CRC.
            if not raw.startswith(PNG_SIGNATURE):raise ValueError(f'{original.filename} is not PNG')
            source_bytes+=len(raw)
            if transform:
                raw,stats=process_png(raw,pipeline)
                sw,sh=stats['source_size'];ow,oh=stats['output_size']
                if ow>sw or oh>sh:raise ValueError('thumbnail was upscaled')
                if abs(sw*oh-sh*ow)>max(sw,sh):raise ValueError('thumbnail aspect ratio changed')
                source_sizes[f'{sw}x{sh}']+=1;output_sizes[f'{ow}x{oh}']+=1;alpha[stats['source_alpha']]+=1
                indexed_count+=int(stats['indexed']);transparent_count+=int(stats['transparent_RGBA_preserved'])
                quantizers[stats['quantizer']or'RGBA unchanged']+=1
                pixel_hashes[original.filename]=stats['rgba_sha256']
            info=zipfile.ZipInfo(original.filename,date_time=original.date_time)
            info.compress_type=compression_type;info.external_attr=original.external_attr
            info.internal_attr=original.internal_attr;info.create_system=original.create_system
            zout.writestr(info,raw,compress_type=compression_type,compresslevel=6 if compression_type else None)
            hashes[original.filename]=hashlib.sha256(raw).hexdigest();plain_bytes+=len(raw)
    # Verify every output image, not just ZIP entry counts or first entries.
    with zipfile.ZipFile(dest,'r')as z:
        infos=z.infolist()
        if [i.filename for i in infos] != [i.filename for i in entries]:raise ValueError('output ZIP order/names changed')
        for info in infos:
            decoded=z.read(info)
            if hashlib.sha256(decoded).hexdigest()!=hashes[info.filename]:
                raise ValueError(f'PNG bytes changed: {info.filename}')
            if transform:
                from PIL import Image
                import io
                with Image.open(io.BytesIO(decoded))as image:
                    image.load()
                    if hashlib.sha256(image.convert('RGBA').tobytes()).hexdigest()!=pixel_hashes[info.filename]:
                        raise ValueError(f'PNG pixel roundtrip mismatch: {info.filename}')
    return dict(count=len(entries),uncompressed_png_bytes=plain_bytes,
                source_uncompressed_png_bytes=source_bytes,
                png_bytes_equal=not transform,processed_png_bytes_equal=True,
                all_entries_crc_and_sha256_checked=True,all_output_PNGs_decoded=transform,
                pipeline=pipeline,source_size_distribution=dict(source_sizes),output_size_distribution=dict(output_sizes),
                source_alpha_distribution=dict(alpha),indexed_png_count=indexed_count,
                quantizer_counts=dict(quantizers),
                transparent_RGBA_preserved_count=transparent_count,
                never_upscaled=True,aspect_ratio_preserved=True,
                compression=compression,format='ZIP archive of ID.png files; .bin extension; PNG processing is declared in pipeline')

def create_database(path,items,sets,meta,item_meta=()):
    con=sqlite3.connect(path)
    try:
        con.execute('PRAGMA foreign_keys=ON')
        con.execute(f'PRAGMA application_id={APPLICATION_ID}')
        con.execute(f'PRAGMA user_version={SCHEMA_VERSION}')
        con.executescript('''
        CREATE TABLE meta(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL);
        CREATE TABLE items(
          id INTEGER PRIMARY KEY CHECK(id>0),name TEXT NOT NULL,jpName TEXT NOT NULL,
          brand INTEGER NOT NULL,brandName TEXT NOT NULL,category TEXT NOT NULL,
          model TEXT NOT NULL,isDress INTEGER NOT NULL CHECK(isDress IN(0,1)),
          sourceRow INTEGER NOT NULL,thumbnail INTEGER NOT NULL CHECK(thumbnail IN(0,1)),
          search TEXT NOT NULL,nameSource TEXT);
        CREATE TABLE sets(
          id TEXT PRIMARY KEY NOT NULL,name TEXT NOT NULL,brand INTEGER NOT NULL,
          brandName TEXT NOT NULL,search TEXT NOT NULL,
          top INTEGER NOT NULL CHECK(top>=0),bottom INTEGER NOT NULL CHECK(bottom>=0),
          shoes INTEGER NOT NULL CHECK(shoes>=0),head INTEGER NOT NULL CHECK(head>=0));
        CREATE TABLE item_meta(id INTEGER PRIMARY KEY,grade INTEGER NOT NULL CHECK(grade>=0),hidden INTEGER NOT NULL CHECK(hidden IN(0,1)));
        CREATE INDEX items_category_brand_name ON items(category,brand,name COLLATE NOCASE);
        CREATE INDEX items_brand_category ON items(brand,category);
        CREATE INDEX items_model ON items(model);
        CREATE INDEX items_source_row ON items(sourceRow);
        CREATE INDEX sets_brand_name ON sets(brand,name COLLATE NOCASE);
        CREATE INDEX sets_top ON sets(top);
        CREATE INDEX sets_bottom ON sets(bottom);
        CREATE INDEX sets_shoes ON sets(shoes);
        CREATE INDEX sets_head ON sets(head);
        ''')
        # Keep source value0 as the empty slot sentinel. SQLite's ordinary FK
        # cannot permit0 without adding a fake item row. These triggers enforce
        # real nonzero references while preserving all source values/counts.
        condition=' OR '.join(f'(NEW.{slot}<>0 AND NOT EXISTS(SELECT 1 FROM items WHERE id=NEW.{slot}))'for slot in SLOTS)
        for event in ('INSERT','UPDATE'):
            con.execute(f"CREATE TRIGGER sets_reference_{event.lower()} BEFORE {event} ON sets WHEN {condition} BEGIN SELECT RAISE(ABORT,'set references missing item'); END")
        used=' OR '.join(f'{slot}=OLD.id'for slot in SLOTS)
        con.execute(f"CREATE TRIGGER items_reference_delete BEFORE DELETE ON items WHEN EXISTS(SELECT 1 FROM sets WHERE {used}) BEGIN SELECT RAISE(ABORT,'item referenced by set'); END")
        con.execute(f"CREATE TRIGGER items_reference_update BEFORE UPDATE OF id ON items WHEN NEW.id<>OLD.id AND EXISTS(SELECT 1 FROM sets WHERE {used}) BEGIN SELECT RAISE(ABORT,'item referenced by set'); END")
        with con:
            con.executemany('INSERT INTO meta(key,value) VALUES(?,?)',sorted(meta.items()))
            con.executemany('INSERT INTO items('+','.join(ITEM_FIELDS)+') VALUES('+','.join('?'for _ in ITEM_FIELDS)+')',items)
            con.executemany('INSERT INTO sets('+','.join(SET_FIELDS)+') VALUES('+','.join('?'for _ in SET_FIELDS)+')',sets)
            con.executemany('INSERT INTO item_meta(id,grade,hidden) VALUES(?,?,?)',item_meta)
        con.execute('VACUUM')
    finally:con.close()

def verify_database(path,catalog,items,sets,item_meta=()):
    con=sqlite3.connect(Path(path).resolve().as_uri()+'?mode=ro',uri=True)
    try:
        con.execute('PRAGMA query_only=ON')
        if con.execute('PRAGMA application_id').fetchone()[0]!=APPLICATION_ID:raise ValueError('application_id mismatch')
        if con.execute('PRAGMA user_version').fetchone()[0]!=SCHEMA_VERSION:raise ValueError('user_version mismatch')
        integrity=[r[0]for r in con.execute('PRAGMA integrity_check')]
        if integrity!=['ok']:raise ValueError(f'SQLite integrity_check failed: {integrity}')
        if list(con.execute('PRAGMA foreign_key_check')):raise ValueError('SQLite foreign_key_check failed')
        actual_items={r[0]:r for r in con.execute('SELECT '+','.join(ITEM_FIELDS)+' FROM items')}
        actual_sets={r[0]:r for r in con.execute('SELECT '+','.join(SET_FIELDS)+' FROM sets')}
        if list(con.execute('SELECT id,grade,hidden FROM item_meta ORDER BY id'))!=sorted(item_meta):raise ValueError('item display metadata mismatch')
        if len(actual_items)!=len(items)or any(actual_items.get(r[0])!=r for r in items):raise ValueError('item field roundtrip mismatch')
        if len(actual_sets)!=len(sets)or any(actual_sets.get(r[0])!=r for r in sets):raise ValueError('set field roundtrip mismatch')
        for slot in SLOTS:
            missing=con.execute(f'SELECT s.id,s.{slot} FROM sets s LEFT JOIN items i ON i.id=s.{slot} WHERE s.{slot}<>0 AND i.id IS NULL').fetchall()
            if missing:raise ValueError(f'nonzero {slot} reference errors: {missing[:5]}')
        meta=dict(con.execute('SELECT key,value FROM meta'))
        if meta.get('schema_version')!=str(SCHEMA_VERSION) or meta.get('item_count')!=str(len(items)) or meta.get('set_count')!=str(len(sets)):
            raise ValueError('schema/count metadata mismatch')
        if meta['source_version']!=json_text(catalog.get('version'))or json.loads(meta['source_meta_json'])!=catalog.get('meta'):
            raise ValueError('source version/meta mismatch')
        root_extra={k:v for k,v in catalog.items()if k not in ('items','sets','version','meta')}
        if json.loads(meta['source_root_extra_fields_json'])!=root_extra:raise ValueError('root extra fields mismatch')
        if json.loads(meta['source_item_order_json'])!=[r['id']for r in catalog['items']]:raise ValueError('item order provenance mismatch')
        if json.loads(meta['source_set_order_json'])!=[r['id']for r in catalog['sets']]:raise ValueError('set order provenance mismatch')
        # Reconstruct the complete original JSON rows, including optional/new
        # fields not consumed by the current loader, and compare every value.
        extras=json.loads(meta['source_item_extra_fields_json']);set_extras=json.loads(meta['source_set_extra_fields_json'])
        for original in catalog['items']:
            row=dict(zip(ITEM_FIELDS,actual_items[original['id']]))
            row['isDress']=bool(row['isDress']);row['thumbnail']=bool(row['thumbnail'])
            if 'nameSource'not in original:row.pop('nameSource')
            row.update(extras.get(str(original['id']),{}))
            if row!=original:raise ValueError(f'full item JSON roundtrip mismatch {original["id"]}')
        for original in catalog['sets']:
            row=dict(zip(SET_FIELDS,actual_sets[original['id']]))
            row['pieces']={slot:row.pop(slot)for slot in SLOTS}
            extra=dict(set_extras.get(original['id'],{}));row['pieces'].update(extra.pop('pieces',{}));row.update(extra)
            if row!=original:raise ValueError(f'full set JSON roundtrip mismatch {original["id"]}')
        return dict(integrity_check=integrity,sqlite_foreign_key_check=[],nonzero_set_references_checked=True,
                    zero_empty_slot_sentinel_preserved=True,all_item_fields_equal=True,all_set_fields_equal=True,
                    complete_source_JSON_rows_reconstructed_equal=True,source_meta_and_order_preserved=True,
                    indexes=[r[0]for r in con.execute("SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%' ORDER BY name")])
    finally:con.close()

def export_split(catalog_path,thumbs_path,out_dir,*,data_version=None,compression='deflate',overwrite=False,image_max_side=128,image_colors=256,image_quantizer='adaptive',set_representatives=None):
    start=time.perf_counter();catalog_path=Path(catalog_path).resolve();out_dir=Path(out_dir).resolve()
    thumbs_path=Path(thumbs_path).resolve()if thumbs_path else None
    outputs=[out_dir/DATABASE_FILE]+([out_dir/IMAGES_FILE]if thumbs_path else[])
    for target in outputs:
        if target==catalog_path or target==thumbs_path:raise ValueError('output must not overwrite an input')
        if target.exists()and not overwrite:raise FileExistsError(f'{target} exists; use --overwrite for an intentional data update')
    original_hashes={str(p):sha256(p)for p in (catalog_path,thumbs_path)if p}
    catalog,items,sets,item_extras,set_extras=read_catalog(catalog_path)
    if set_representatives is None:set_representatives=Path(__file__).resolve().parent.parent/'assets/set_representative_ids.json'
    hidden=set(json.loads(Path(set_representatives).read_text(encoding='utf8'))['ids'])if Path(set_representatives).exists()else set()
    item_meta=[]
    for row in catalog['items']:
        grade=row.get('grade',0)
        if not isinstance(grade,int)or isinstance(grade,bool)or grade<0:raise ValueError(f'invalid grade for item {row["id"]}')
        if grade or row['id']in hidden:item_meta.append((row['id'],grade,int(row['id']in hidden)))
    ids={r[0]for r in items};declared={r[0]for r in items if r[9]}
    entries=inspect_images(thumbs_path,ids,declared)if thumbs_path else[]
    catalog_sha=original_hashes[str(catalog_path)]
    image_sha=original_hashes[str(thumbs_path)]if thumbs_path else''
    pipeline=image_pipeline(image_max_side,image_colors,image_quantizer)
    if data_version is None:
        data_version='arcade-'+catalog_sha[:12]+('-'+image_sha[:8]if image_sha else'-no-images')
        if thumbs_path and (image_max_side or image_colors):data_version+=f'-thumb{image_max_side or "original"}-c{image_colors or "RGBA"}-{image_quantizer}'
    data_version=text(str(data_version),'data_version')
    meta=dict(schema_version=str(SCHEMA_VERSION),data_version=str(data_version),
              item_count=str(len(items)),set_count=str(len(sets)),image_count=str(len(entries)),
              source_version=json_text(catalog.get('version')),source_meta_json=json_text(catalog.get('meta')),
              source_catalog_sha256=catalog_sha,source_images_sha256=image_sha,
              source_item_order_json=json_text([r['id']for r in catalog['items']]),
              source_set_order_json=json_text([r['id']for r in catalog['sets']]),
              source_item_extra_fields_json=json_text(item_extras),source_set_extra_fields_json=json_text(set_extras),
              source_root_extra_fields_json=json_text({k:v for k,v in catalog.items()if k not in ('items','sets','version','meta')}),
              image_container='zip-png-id-v1',empty_piece_id='0',image_processing_json=json_text(pipeline))
    out_dir.mkdir(parents=True,exist_ok=True)
    # Temporary staging belongs solely to this exporter. Outputs become visible
    # only after the complete SQLite and image-byte checks have succeeded.
    with tempfile.TemporaryDirectory(prefix='.split-export-',dir=out_dir)as temporary:
        staging=Path(temporary);db=staging/DATABASE_FILE;images=staging/IMAGES_FILE
        image_result=build_images(thumbs_path,images,entries,compression,max_side=image_max_side,colors=image_colors,quantizer=image_quantizer)if thumbs_path else dict(count=0,optional_images_omitted=True)
        create_database(db,items,sets,meta,item_meta);db_result=verify_database(db,catalog,items,sets,item_meta)
        if any(sha256(p)!=h for p,h in original_hashes.items()):raise ValueError('input changed during export')
        report=dict(passed=True,schema_version=SCHEMA_VERSION,application_id=f'0x{APPLICATION_ID:08X}',
                    data_version=str(data_version),source=dict(catalog=str(catalog_path),images=str(thumbs_path)if thumbs_path else None,sha256=original_hashes),
                    counts=dict(items=len(items),sets=len(sets),images=len(entries),declared_thumbnails=len(declared)),
                    database=db_result,images=image_result,source_files_unchanged=True,
                    output=dict(database=dict(name=DATABASE_FILE,bytes=db.stat().st_size,sha256=sha256(db)),
                                images=dict(name=IMAGES_FILE,bytes=images.stat().st_size,sha256=sha256(images))if thumbs_path else None),
                    elapsed_seconds=round(time.perf_counter()-start,3),sqlite_version=sqlite3.sqlite_version)
        os.replace(db,out_dir/DATABASE_FILE)
        if thumbs_path:os.replace(images,out_dir/IMAGES_FILE)
        (out_dir/'data_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    return report

def main(argv=None):
    here=Path(__file__).resolve().parent
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalog',type=Path,required=True,help='Explicit input catalog JSON; no private research paths are assumed')
    parser.add_argument('--thumbs',type=Path,help='Explicit optional input ZIP of ID.png images')
    parser.add_argument('--without-images',action='store_true',help='Export a complete DB without creating the optional PNG ZIP companion')
    parser.add_argument('--out-dir',type=Path,required=True,help='Explicit output directory for data.db and optional images.bin')
    parser.add_argument('--data-version',help='Editable data release label; default derives from input SHA256 values')
    parser.add_argument('--compression',choices=('deflate','store'),default='deflate')
    parser.add_argument('--image-max-side',type=int,default=128,help='Maximum thumbnail side in pixels, never upscale;0 disables resizing')
    parser.add_argument('--image-colors',type=int,default=256,help='Opaque PNG palette colors1..256;0 keeps RGBA. Transparent images retain RGBA')
    parser.add_argument('--image-quantizer',choices=('adaptive','fast','median'),default='adaptive',help='Adaptive preserves pale details with MEDIANCUT when FASTOCTREE collapses them into white')
    parser.add_argument('--original-images',action='store_true',help='Copy exact original PNG bytes; ignores image size/color switches and requires no Pillow')
    parser.add_argument('--set-representatives',type=Path,help='Optional audited representative ID JSON; defaults to the bundled nonprivate ID list')
    parser.add_argument('--overwrite',action='store_true',help='Intentionally replace this tool\'s existing output companions')
    args=parser.parse_args(argv)
    if not args.without_images and args.thumbs is None:
        parser.error('--thumbs is required unless --without-images is selected')
    report=export_split(args.catalog,None if args.without_images else args.thumbs,args.out_dir,
                        data_version=args.data_version,compression=args.compression,overwrite=args.overwrite,
                        image_max_side=None if args.original_images or args.image_max_side==0 else args.image_max_side,
                        image_colors=None if args.original_images or args.image_colors==0 else args.image_colors,
                        image_quantizer=args.image_quantizer,set_representatives=args.set_representatives)
    print(json.dumps(dict(passed=report['passed'],counts=report['counts'],output=report['output'],elapsed_seconds=report['elapsed_seconds']),ensure_ascii=False))
    return 0

if __name__=='__main__':
    raise SystemExit(main())
