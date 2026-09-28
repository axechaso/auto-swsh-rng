# Native OCR compatibility resources

The DLLs and Chinese language data are copied without modification from
[auto-bdsp-rng packaging/easycon_native](https://github.com/XiaoyuBook/auto-bdsp-rng/tree/eb1e65226fe678dd6e30f48e5d5fbbdeec566ba8/packaging/easycon_native)
at commit `eb1e65226fe678dd6e30f48e5d5fbbdeec566ba8`.
They support existing EasyCon `.IL` TesserDetect labels on Windows x64.

- `x64/tesseract50.dll`: Tesseract 5.0, Apache-2.0. [Source](https://github.com/tesseract-ocr/tesseract/tree/5.0.0), license in `licenses/tesseract-Apache-2.0.txt`.
- `x64/leptonica-1.82.0.dll`: Leptonica 1.82.0, BSD-2-Clause. [Source](https://github.com/DanBloomberg/leptonica/tree/1.82.0), copyright notice and license in `licenses/leptonica-BSD-2-Clause.txt`.
- `Tessdata/chi_sim.traineddata`: Tesseract simplified Chinese recognition data, Apache-2.0. [Upstream data](https://github.com/tesseract-ocr/tessdata), license in `licenses/tessdata-Apache-2.0.txt`.

These third-party files retain their original licenses. The standalone PaddleOCR
page installs its own Python dependencies and models separately.
