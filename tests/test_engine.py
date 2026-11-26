import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from pypdf import PdfReader, PdfWriter
from PIL import Image, ImageChops, ImageDraw, ImageFilter

from engine import handle


class EngineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.original = self.root / '원본.pdf'
        writer = PdfWriter()
        for width in (100, 200, 300):
            writer.add_blank_page(width, 400)
        writer.write(self.original)

    def test_merge_reordered_inputs_uses_initial_file_folder_and_reserves_names(self):
        other = self.root / 'other'
        other.mkdir()
        image = other / 'photo.png'
        Image.new('RGBA', (50, 100), (255, 40, 40, 80)).save(image)
        prepared = handle({'op': 'prepare', 'path': str(image), 'out': str(other / 'image.pdf')})
        self.assertEqual(prepared['count'], 1)
        args = {'op': 'merge', 'anchor': str(self.original), 'files': [prepared['path'], str(self.original)]}
        first = Path(handle(args)['path'])
        second = Path(handle(args)['path'])
        self.assertEqual(first, self.root / '원본_병합.pdf')
        self.assertEqual(second, self.root / '원본_병합 (2).pdf')
        widths = [int(p.mediabox.width) for p in PdfReader(first).pages]
        self.assertEqual(widths[1:], [100, 200, 300])
        self.assertEqual(len(widths), 4)

    def test_delete_selected_pages_keeps_original_and_rejects_empty_pdf(self):
        args = {'op': 'delete', 'path': str(self.original), 'pages': [0, 2]}
        edited = Path(handle(args)['path'])
        self.assertEqual(edited.name, '원본_편집됨.pdf')
        self.assertEqual([int(p.mediabox.width) for p in PdfReader(edited).pages], [200])
        self.assertEqual(len(PdfReader(self.original).pages), 3)
        with self.assertRaises(ValueError):
            handle(dict(args, pages=[0, 1, 2]))
        self.assertEqual(len(list(self.root.glob('*편집됨*'))), 1)

    def test_failed_merge_does_not_leave_a_partial_output(self):
        with self.assertRaises(Exception):
            handle({'op': 'merge', 'anchor': str(self.original), 'files': [str(self.original), str(self.root / 'missing.pdf')]})
        self.assertEqual(list(self.root.glob('*병합*')), [])

    def test_single_pdf_merge_saves_a_separate_result(self):
        args = {'op': 'merge', 'anchor': str(self.original), 'files': [str(self.original)]}
        result = handle(args)
        merged = Path(result['path'])
        self.assertEqual(merged, self.root / '원본_병합.pdf')
        self.assertEqual(result['count'], 3)
        self.assertEqual([int(page.mediabox.width) for page in PdfReader(merged).pages], [100, 200, 300])
        self.assertTrue(self.original.is_file())
        with self.assertRaises(ValueError):
            handle(dict(args, files=[]))

    def test_jfif_can_be_merged_as_one_pdf_page(self):
        image = self.root / '사진.JFIF'
        Image.new('RGB', (120, 80), (42, 75, 190)).save(image, 'JPEG')
        with Image.open(image) as source:
            self.assertIn('jfif', source.info)
        prepared = handle({'op': 'prepare', 'path': str(image), 'out': str(self.root / 'image.pdf')})
        self.assertEqual(prepared['count'], 1)
        result = handle({'op': 'merge', 'anchor': str(image), 'files': [prepared['path'], str(self.original)]})
        self.assertEqual(result['count'], 4)
        self.assertTrue(image.is_file())

    def test_render_honors_size_and_returns_page_count(self):
        out = self.root / 'preview.png'
        result = handle({'op': 'render', 'path': str(self.original), 'page': 1, 'pixels': 250, 'out': str(out)})
        self.assertEqual(result['count'], 3)
        with Image.open(out) as image:
            self.assertEqual(image.size, (125, 250))

    def test_pdf_can_be_read_when_resolving_its_path_fails(self):
        out = self.root / 'preview.png'
        with patch.object(Path, 'resolve', side_effect=ValueError('stat: embedded null character in path')):
            self.assertEqual(handle({'op': 'count', 'path': str(self.original)})['count'], 3)
            result = handle({'op': 'render', 'path': str(self.original), 'page': 1, 'pixels': 250, 'out': str(out)})
        self.assertEqual(result['count'], 3)
        self.assertTrue(out.is_file())

    def test_sharp_view_increases_edge_contrast_without_changing_page_size(self):
        source = self.root / 'scan.pdf'
        image = Image.new('L', (120, 120), 230)
        ImageDraw.Draw(image).rectangle((30, 30, 90, 90), fill=90)
        image.filter(ImageFilter.GaussianBlur(1.5)).save(source, 'PDF')
        request = {'op': 'render', 'path': str(source), 'page': 0, 'pixels': 480}
        plain, sharp = self.root / 'plain.png', self.root / 'sharp.png'
        handle(dict(request, out=str(plain)))
        handle(dict(request, out=str(sharp), sharpen=True))
        with Image.open(plain) as before, Image.open(sharp) as after:
            self.assertEqual(before.size, after.size)
            self.assertIsNotNone(ImageChops.difference(before, after).getbbox())
            def peak_contrast(img):
                with img.convert('L') as gray:
                    return max(gray.getpixel((x, 240)) - gray.getpixel((x + 1, 240)) for x in range(105, 136))
            self.assertGreater(peak_contrast(after), peak_contrast(before))
