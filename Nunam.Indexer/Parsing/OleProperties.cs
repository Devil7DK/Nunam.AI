using System.Buffers.Binary;
using System.Text;
using OpenMcdf;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Reads title, author, subject and word count from an OLE file's SummaryInformation stream ([MS-OLEPS]).</summary>
    public static class OleProperties
    {
        public record Summary(string? Title, string? Subject, string? Author, int? Words)
        {
            public static Summary None { get; } = new(null, null, null, null);
        }

        private const int CodePage = 1, Title = 2, Subject = 3, Author = 4, WordCount = 15;
        private const ushort VtI2 = 2, VtI4 = 3, VtLpstr = 30, VtLpwstr = 31;

        /// <summary>Whatever could be read; a missing or malformed stream gives <see cref="Summary.None"/>.</summary>
        public static Summary ReadSummary(RootStorage root)
        {
            try
            {
                if (!root.TryOpenStream("\x05SummaryInformation", out var stream)) return Summary.None;
                byte[] data;
                using (stream)
                {
                    if (stream.Length > 1024 * 1024) return Summary.None;
                    data = new byte[stream.Length];
                    stream.ReadExactly(data);
                }
                return Parse(data);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or FormatException or IndexOutOfRangeException)
            {
                return Summary.None;
            }
        }

        private static Summary Parse(byte[] bytes)
        {
            ReadOnlySpan<byte> data = bytes;
            // Header (28 bytes), then the first section's FMTID (16) and offset (4).
            var section = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[44..]);
            var count = BinaryPrimitives.ReadInt32LittleEndian(data[(section + 4)..]);

            var offsets = new Dictionary<int, int>();
            for (var i = 0; i < count && i < 256; i++)
            {
                var entry = section + 8 + i * 8;
                offsets[BinaryPrimitives.ReadInt32LittleEndian(data[entry..])] =
                    section + BinaryPrimitives.ReadInt32LittleEndian(data[(entry + 4)..]);
            }

            var encoding = Encoding.UTF8;
            if (offsets.TryGetValue(CodePage, out var at) && BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) == VtI2)
            {
                var codePage = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);
                encoding = codePage == 1200 ? Encoding.Unicode
                    : CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.UTF8;
            }

            string? String(int id)
            {
                ReadOnlySpan<byte> data = bytes;
                if (!offsets.TryGetValue(id, out var p)) return null;
                var type = BinaryPrimitives.ReadUInt16LittleEndian(data[p..]);
                var length = BinaryPrimitives.ReadInt32LittleEndian(data[(p + 4)..]);
                if (length <= 0 || length > 64 * 1024) return null;
                var value = type switch
                {
                    VtLpstr => encoding.GetString(data.Slice(p + 8, length)),
                    VtLpwstr => Encoding.Unicode.GetString(data.Slice(p + 8, length * 2)),
                    _ => null,
                };
                value = value?.TrimEnd('\0').Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }

            int? Number(int id) =>
                offsets.TryGetValue(id, out var p) && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p)) == VtI4
                    && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p + 4)) is > 0 and var n
                    ? n
                    : null;

            return new Summary(String(Title), String(Subject), String(Author), Number(WordCount));
        }
    }
}
