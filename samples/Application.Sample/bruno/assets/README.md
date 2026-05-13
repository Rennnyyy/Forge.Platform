# Bruno collection assets

Binary files referenced by `body:file` directives in the Bruno requests.

| File | Used by | Description |
|------|---------|-------------|
| `sample-track-master.wav` | `19-track-masters/05-upload-content.bru`<br>`19-track-masters/06-re-upload-blocked.bru` | Minimal valid WAV (8 kHz, mono, 8-bit PCM, 0.1 s silence). Swap for a real audio file to exercise larger uploads. |

## Swapping in a real file

Replace `sample-track-master.wav` with any valid WAV file, or point the requests at a
different relative path by editing the `@file(...)` directive in the `.bru` files.

The server streams the body directly into the object store — there is no in-process
buffering — so large files work without changing any server configuration.

Bruno sends this as a raw binary body (`body: binary` + `@file(...)`), which maps
directly to `ctx.Request.Body` on the server. No multipart/form-data wrapping is used.
