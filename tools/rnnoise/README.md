# rnnoise.dll

`src/Shabpa/Native/rnnoise.dll` is [RNNoise](https://github.com/xiph/rnnoise) v0.1.1 (BSD-3-Clause, Xiph.Org / Mozilla), built for x64 with its built-in model.

The source is the unmodified release tarball except for `msvc-alloca.patch`, which replaces five variable-length arrays with `_alloca` so that MSVC can compile it.

To rebuild (Visual Studio Build Tools, x64 developer prompt), in the extracted `rnnoise-0.1.1` folder:

```
git apply msvc-alloca.patch        (or patch -p0)
echo. > w\config.h
cl /O2 /LD /DRNNOISE_BUILD /DDLL_EXPORT /D_USE_MATH_DEFINES /Iw /Iinclude /Isrc src\denoise.c src\kiss_fft.c src\pitch.c src\celt_lpc.c src\rnn.c src\rnn_data.c /Fe:rnnoise.dll /link /DEF:rn.def
```
