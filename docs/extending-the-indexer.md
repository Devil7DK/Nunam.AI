# Extending the indexer

Every file the indexer reads goes through a named **parser**; images and scanned PDFs also use an **OCR engine** and a **PDF renderer**. All three can be swapped per machine: for another built-in, for any program on the machine, or for a .NET plugin.

## Built-in providers

| Parser | Reads |
|---|---|
| `text` | Plain text, source code, markup, CSV and similar, as UTF-8 |
| `pdfpig` | PDF text and details; renders and OCRs PDFs that are scans |
| `word97` | Word 97–2003 `.doc` and `.dot` |
| `openxml-word` | Word 2007 and later: `.docx`, `.docm`, `.dotx`, `.dotm` |
| `rtf` | Rich Text Format |
| `exceldatareader` | Excel `.xls`, `.xlsx`, `.xlsm`, `.xltx`, `.xltm` |
| `openxml-powerpoint` | PowerPoint 2007 and later: `.pptx` and its variants |
| `image` | Photo details (camera, date, GPS), OCR and a preview for the vision model |
| `taglib` | Song and recording tags |
| `ffmpeg` | Video details and a preview frame (needs ffmpeg) |
| `fallback` | Name and file details only; always tried last |

OCR engines: `tesseract` (the default) and `none`. PDF renderers: `pdfium` (the default) and `none`.

## Choosing parsers

`Indexer:Parsers` lists, per extension, the parsers to try in order. The first one that reads the file wins. A parser that declines the file, or fails, passes it to the next. If one failed and nothing after it succeeded, the file is marked as failed with that error rather than indexed by name, so a broken file shows up in *Index status*.

```json
"Indexer": {
  "Parsers": {
    ".doc": ["word97", "command:antiword"],
    ".rtf": ["fallback"]
  },
  "OcrEngine": "tesseract",
  "PdfRenderer": "pdfium"
}
```

Extensions that aren't listed get every parser that offers itself for them: plugins and commands first, then the built-in ones in the order of the table above.

With remote management on, the **Machines** page can change `Parsers`, `OcrEngine` and `PdfRenderer`, but only to built-in providers. Commands and plugins can only be named in the machine's own `appsettings.Local.json`, because choosing them chooses what the indexer runs.

## External commands

Any program that prints a file's text works as a parser. Define it under `Indexer:Commands` and refer to it as `command:<name>`:

```json
"Indexer": {
  "Commands": {
    "antiword": { "Path": "/usr/bin/antiword", "Arguments": ["{file}"] },
    "pandoc": { "Path": "/usr/bin/pandoc", "Arguments": ["-t", "plain", "{file}"], "Extensions": [".odt", ".epub"] },
    "catppt": { "Path": "/usr/bin/catppt", "Extensions": [".ppt"] }
  }
}
```

| Field | |
|---|---|
| `Path` | Full path of the program. |
| `Arguments` | Passed as they are; `{file}` becomes the file's full path. Leave it out for just the path. |
| `Extensions` | Offer the command for these extensions when `Parsers` doesn't list them. |
| `Stdin` | `true` sends the file's bytes on standard input. |
| `TimeoutSeconds` | 120 by default. |
| `Use` | `parser` (default) or `ocr`. |

The program's standard output becomes the file's text, up to the content limit. A non-zero exit code counts as a failure, so the next parser gets the file. Commands run as the indexer's service account and inside its sandbox, so they can read only what the indexer can.

An OCR command (`"Use": "ocr"`) gets the image (PNG or JPEG) on standard input; `{lang}` in its arguments becomes `Indexer:OcrLanguage`. Select it with `"OcrEngine": "command:<name>"`.

## Plugins

A plugin is a .NET 10 class library that implements one or more of the interfaces in `Nunam.Indexer.Abstractions`:

- `IFileParser`: a `Name`, `CanParse(kind, extension)` and `ParseAsync(request, cancellationToken)`. Return `ParseOutcome.Declined` to pass the file on.
- `IOcrEngine`: reads text out of an image.
- `IPdfRenderer`: renders PDF pages to PNG.

`samples/Nunam.Indexer.Plugins.OpenDocument` in the repository is a complete example: it reads LibreOffice's `.odt`, `.ods` and `.odp` files. Its project file shows the two settings a plugin needs:

```xml
<EnableDynamicLoading>true</EnableDynamicLoading>
...
<ProjectReference Include="..\..\Nunam.Indexer.Abstractions\Nunam.Indexer.Abstractions.csproj">
  <Private>false</Private>
  <ExcludeAssets>runtime</ExcludeAssets>
</ProjectReference>
```

To install one, publish it (`dotnet publish -c Release`) and copy the output into a folder named after its assembly, inside the plugin folder:

- Windows: `C:\Program Files\Nunam\Indexer\plugins\<Plugin>\<Plugin>.dll`
- Linux: `/usr/lib/nunam-indexer/plugins/<Plugin>/<Plugin>.dll`

`Indexer:PluginFolder` points somewhere else. Restart the service; its log lists each plugin and the providers it found. Constructors can ask for `ILogger<T>`, `IConfiguration` or `IOptions<IndexerOptions>`.

A plugin runs inside the indexer with the service account's access to every watched file, so install only plugins you trust, and keep the plugin folder writable by administrators only. Previews a plugin returns are scaled and re-encoded by the indexer.
