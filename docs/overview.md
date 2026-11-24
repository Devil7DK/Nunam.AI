# How Nunam works

Nunam finds files on every computer in your home by what is in them: the words in a document, the text in a scanned bill, or what a photo shows. It runs on your own hardware, and nothing is sent to an outside service.

## The parts

| Part | Runs on | What it does |
|---|---|---|
| **Indexer** | each computer with files (Windows, Linux) | Scans the folders you choose, watches them for changes, and reads each file locally: text from PDFs, Office files and plain text, OCR of images and scanned pages, tags of songs, details of videos and photos. Sends what it finds to the server. |
| **Database** | the server | Postgres with pgvector. Holds the extracted text, file details, a scaled-down preview of each image, and the vectors used for search by meaning. |
| **AI worker** | the server | Sends each preview to a vision model for a short description, then turns the text and description into a vector with an embedding model. |
| **Ollama** | a machine with a GPU | Runs the two models. Any Ollama server on your network works, or an OpenAI-compatible one such as LM Studio. |
| **API and web UI** | the server | Search, previews, accounts and sharing, and the *Machines* page to add machines and see each indexer's status, logs and settings. Indexers and the AI worker reach the database only through the API. |

## What leaves each computer

The indexer reads files where they are and never copies them. It sends the server only:

- the path, size, dates and type of each file;
- the text it could extract, capped at 500,000 characters per file;
- for images, scanned PDFs and videos, one preview image of at most 1568 pixels on its longest side;
- details such as page count, camera model, GPS position or song title.

Opening a result shows that preview and text. The file itself stays on its computer.

## Searching

A search runs two ways at once and merges the two rankings:

- **Keywords** match file names and the words inside documents, OCR text and AI descriptions, with highlighted snippets.
- **Meaning** compares the query's vector with each file's, so "when is the electricity bill due" finds a scanned bill, and "photo from a holiday" finds the photo, even when no word matches.

Either can also be used alone, and results can be narrowed by type, machine, extension and date. If Ollama is unreachable, search falls back to keywords, and new files wait in the queue until it is back.

## Who sees what

Every person signs in with their own account. Admins see every machine. Everyone else sees only the machines shared with them, and the owner of a machine decides who that is. Each indexer signs in to the API with its own machine token, which reads and writes only its own machine's files. Removing a machine in the UI revokes its token.

## Getting started

1. [Deploy the server](deployment.md): Postgres, the API, the web UI and the AI worker in Docker.
2. [Add each computer](adding-a-machine.md): copy the install command from *Machines* → *Add machine*, run it there, and choose its folders.
