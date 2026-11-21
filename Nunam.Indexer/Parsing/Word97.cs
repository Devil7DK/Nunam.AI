using System.Buffers.Binary;
using System.Text;
using OpenMcdf;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Reads the text of a binary Word 97–2003 document (.doc, .dot) from its piece table ([MS-DOC]
    /// 2.4.1). Formatting, headers and footnotes are ignored; field codes are dropped and their results kept.
    /// </summary>
    public static class Word97
    {
        private const ushort WordMagic = 0xA5EC;
        private const ushort FlagEncrypted = 0x0100;
        private const ushort FlagTable1 = 0x0200;
        private const int FcClxIndex = 33;

        private static readonly Encoding Ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

        public record Result(string? Text, bool Encrypted, OleProperties.Summary Summary);

        /// <summary>
        /// Reads the main text, stopping at <paramref name="limit"/> characters. Throws
        /// <see cref="InvalidDataException"/> for files that aren't Word 97 or later.
        /// </summary>
        public static Result Read(Stream file, int limit)
        {
            using var root = RootStorage.Open(file, StorageModeFlags.LeaveOpen);
            var summary = OleProperties.ReadSummary(root);

            var document = ReadAll(root.OpenStream("WordDocument"), maxBytes: 512 * 1024 * 1024);
            if (document.Length < 0x1A2 + 8 || BinaryPrimitives.ReadUInt16LittleEndian(document) != WordMagic)
            {
                throw new InvalidDataException("Not a Word 97 or later document");
            }

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(0x0A));
            if ((flags & FlagEncrypted) != 0) return new Result(null, true, summary);

            // FIB: a 32-byte base, then three variable-length arrays, each prefixed with its count.
            var at = 32;
            at += 2 + BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(at)) * 2;
            var longs = at + 2;
            var mainTextLength = BinaryPrimitives.ReadInt32LittleEndian(document.AsSpan(longs + 3 * 4));
            at += 2 + BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(at)) * 4;
            var pairs = at + 2;
            var fcClx = BinaryPrimitives.ReadInt32LittleEndian(document.AsSpan(pairs + FcClxIndex * 8));
            var lcbClx = BinaryPrimitives.ReadInt32LittleEndian(document.AsSpan(pairs + FcClxIndex * 8 + 4));

            var tableName = (flags & FlagTable1) != 0 ? "1Table" : "0Table";
            var table = ReadAll(root.OpenStream(tableName), maxBytes: 512 * 1024 * 1024);
            if (fcClx < 0 || lcbClx <= 0 || (long)fcClx + lcbClx > table.Length)
            {
                throw new InvalidDataException("Piece table is out of range");
            }

            var text = ReadPieces(document, table.AsSpan(fcClx, lcbClx), mainTextLength, limit);
            return new Result(Clean(text), false, summary);
        }

        private static string ReadPieces(byte[] document, ReadOnlySpan<byte> clx, int mainTextLength, int limit)
        {
            // Clx: any number of Prc (0x01, size-prefixed property changes), then one Pcdt (0x02).
            var at = 0;
            while (at < clx.Length && clx[at] == 0x01)
            {
                at += 3 + BinaryPrimitives.ReadUInt16LittleEndian(clx[(at + 1)..]);
            }
            if (at + 5 > clx.Length || clx[at] != 0x02) throw new InvalidDataException("No piece table");

            var size = BinaryPrimitives.ReadInt32LittleEndian(clx[(at + 1)..]);
            var plc = clx.Slice(at + 5, Math.Min(size, clx.Length - at - 5));
            var pieces = (plc.Length - 4) / 12;

            var text = new StringBuilder();
            for (var i = 0; i < pieces && text.Length < limit; i++)
            {
                var start = BinaryPrimitives.ReadInt32LittleEndian(plc[(i * 4)..]);
                var end = BinaryPrimitives.ReadInt32LittleEndian(plc[((i + 1) * 4)..]);
                if (start >= mainTextLength) break;
                var count = Math.Min(Math.Min(end, mainTextLength) - start, limit - text.Length);
                if (count <= 0) continue;

                var raw = BinaryPrimitives.ReadUInt32LittleEndian(plc[((pieces + 1) * 4 + i * 8 + 2)..]);
                var compressed = (raw & 0x40000000) != 0;
                var fc = (int)(raw & 0x3FFFFFFF);
                var offset = compressed ? fc / 2 : fc;
                var bytes = compressed ? count : count * 2;
                if (offset < 0 || (long)offset + bytes > document.Length) continue;

                text.Append(compressed
                    ? Ansi.GetString(document, offset, bytes)
                    : Encoding.Unicode.GetString(document, offset, bytes));
            }
            return text.ToString();
        }

        /// <summary>
        /// Turns Word's control characters into plain text. A field is <c>0x13 code 0x14 result 0x15</c>;
        /// only the result is text the reader sees. 0x07 ends a table cell, and a second one in a row ends
        /// the table row.
        /// </summary>
        private static string Clean(string raw)
        {
            var text = new StringBuilder(raw.Length);
            var fields = new Stack<bool>();
            var previous = '\0';
            foreach (var c in raw)
            {
                var afterCell = previous == '\x07';
                previous = c;

                switch (c)
                {
                    case '\x13': fields.Push(false); continue;
                    case '\x14': if (fields.Count > 0) { fields.Pop(); fields.Push(true); } continue;
                    case '\x15': if (fields.Count > 0) fields.Pop(); continue;
                }
                if (fields.Count > 0 && !fields.Peek()) continue;

                switch (c)
                {
                    case '\r' or '\x0B' or '\x0C' or '\x0E': text.Append('\n'); break;
                    case '\x07': text.Append(afterCell ? '\n' : '\t'); break;
                    case '\x1E': text.Append('-'); break;
                    case '\t': text.Append('\t'); break;
                    case < ' ': break;
                    default: text.Append(c); break;
                }
            }
            return text.ToString().Trim();
        }

        private static byte[] ReadAll(Stream stream, long maxBytes)
        {
            using (stream)
            {
                if (stream.Length > maxBytes) throw new InvalidDataException("Stream is too large");
                var buffer = new byte[stream.Length];
                stream.ReadExactly(buffer);
                return buffer;
            }
        }
    }
}
