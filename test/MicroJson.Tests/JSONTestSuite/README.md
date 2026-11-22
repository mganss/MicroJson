# JSONTestSuite

The files in `test_parsing/` come from [JSONTestSuite](https://github.com/nst/JSONTestSuite)
by Nicolas Seriot (MIT license, see `LICENSE`). They were taken from commit
`1ef36fa01286573e846ac449e8683f8833c5b26a`.

- `y_*`: the parser must accept the input
- `n_*`: the parser must reject the input
- `i_*`: implementation-defined; the parser may accept or reject the input, but must not crash

The files are stored byte-exact (see `.gitattributes`), because several of them are deliberately
invalid UTF-8 or depend on exact line endings.
