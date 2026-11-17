"""PDF operations for the Windows UI. Requests and replies use one JSON line each."""

import json
import os
import subprocess
import sys
import tempfile
from contextlib import closing
from pathlib import Path

from PIL import Image, ImageOps
from pypdf import PdfReader, PdfWriter
import pypdfium2 as pdfium


IMAGES = {'.jpg', '.jpeg', '.jfif', '.png'}
OFFICE = {'.xlsx', '.xls', '.pptx', '.ppt'}


def page_count(path):
    with pdfium.PdfDocument(path) as doc:
        count = len(doc)
    if not count:
        raise ValueError('페이지가 없는 PDF입니다.')
    return count


def convert_image(path, out):
    with Image.open(path) as source:
        image = ImageOps.exif_transpose(source).convert('RGBA')
        with Image.new('RGB', image.size, 'white') as white:
            white.paste(image, mask=image.getchannel('A'))
            dpi = max(72, max(image.size) / 11.69)
            white.save(out, format='PDF', resolution=dpi)


def convert_office(path, out):
    script = Path(__file__).with_name('office.ps1')
    powershell = Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
    result = subprocess.run(
        [str(powershell), '-NoProfile', '-NonInteractive', '-STA', '-ExecutionPolicy', 'Bypass',
         '-File', str(script), '-SourcePath', str(path), '-OutputPath', str(out)],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, encoding='utf-8',
        errors='replace', creationflags=subprocess.CREATE_NO_WINDOW, timeout=120,
    )
    if result.returncode or not out.is_file():
        message = result.stdout.strip() or result.stderr.strip()
        raise ValueError(message or 'Office에서 PDF를 만들지 못했습니다.')


def publish(writer, anchor, suffix):
    # Write the complete document before claiming a final name. Windows rename
    # fails on an existing destination, including when another instance wins the race.
    fd, name = tempfile.mkstemp(prefix='.pdfmate-', suffix='.tmp', dir=anchor.parent)
    staging = Path(name)
    try:
        with os.fdopen(fd, 'wb') as stream:
            writer.write(stream)
            stream.flush()
            os.fsync(stream.fileno())
        number = 1
        while True:
            counter = '' if number == 1 else f' ({number})'
            result = anchor.with_name(f'{anchor.stem}{suffix}{counter}.pdf')
            try:
                if os.name == 'nt':
                    staging.rename(result)
                else:
                    os.link(staging, result)
                return {'path': str(result), 'count': len(writer.pages)}
            except OSError as error:
                if not isinstance(error, FileExistsError) and not result.exists():
                    raise
                number += 1
    finally:
        staging.unlink(missing_ok=True)


def handle(request, progress=lambda **_: None):
    op = request['op']
    if op == 'merge':
        files = request['files']
        if not files:
            raise ValueError('병합할 파일을 하나 이상 추가하세요.')
        writer = PdfWriter()
        field_names = set()
        for index, path in enumerate(files, start=1):
            progress(done=index - 1, total=len(files), index=index)
            reader = PdfReader(path)
            names = set(reader.get_fields() or ())
            if field_names.intersection(names):
                group = f'PDFMate_{index}'
                while any(name == group or name.startswith(group + '.') for name in field_names):
                    group += '_'
                reader.add_form_topname(group)
                names = set(reader.get_fields() or ())
            field_names.update(names)
            writer.append(reader, import_outline=False)
        progress(done=len(files), total=len(files), index=0)
        return publish(writer, Path(request['anchor']), '_병합')

    path = Path(request['path'])
    if op == 'prepare':
        result = path
        suffix = path.suffix.lower()
        if suffix != '.pdf':
            result = Path(request['out'])
            if suffix in IMAGES:
                convert_image(path, result)
            elif suffix in OFFICE:
                convert_office(path, result)
            else:
                raise ValueError('지원하지 않는 파일 형식입니다.')
        return {'path': str(result), 'count': page_count(result)}

    if op == 'count':
        return {'count': page_count(path)}

    if op == 'delete':
        reader = PdfReader(path)
        removed = set(request['pages'])
        count = len(reader.pages)
        if not removed or not removed.issubset(range(count)) or len(removed) == count:
            raise ValueError('삭제할 페이지를 선택하고 최소 한 페이지를 남겨주세요.')
        writer = PdfWriter()
        writer.append(reader, pages=[index for index in range(count) if index not in removed])
        return publish(writer, path, '_편집됨')

    if op == 'render':
        with pdfium.PdfDocument(path) as doc:
            doc.init_forms()
            with closing(doc[request['page']]) as page:
                scale = request['pixels'] / max(page.get_size())
                with closing(page.render(scale=scale)) as bitmap:
                    with bitmap.to_pil() as image:
                        image.save(request['out'], format='PNG')
            return {'count': len(doc)}

    raise ValueError('지원하지 않는 작업입니다.')


def send(payload):
    print(json.dumps(payload, ensure_ascii=True), flush=True)


if __name__ == '__main__':
    for line in sys.stdin:
        try:
            result = handle(json.loads(line), lambda **info: send({'progress': info}))
            send({'result': result})
        except Exception as error:
            send({'error': str(error) or type(error).__name__})
