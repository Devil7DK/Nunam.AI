using System.Text;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts the visible text of an RTF document: skips font, colour and style tables, pictures,
    /// field instructions and other destinations, and decodes <c>\'hh</c> and <c>\uN</c> characters.
    /// </summary>
    public static class RtfText
    {
        /// <summary>Destinations whose content is never body text.</summary>
        private static readonly HashSet<string> Skipped =
        [
            "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "fldinst", "themedata",
            "colorschememapping", "datastore", "latentstyles", "listtable", "listoverridetable",
            "rsidtbl", "generator", "xmlnstbl", "mmathPr", "filetbl", "revtbl", "pgdsctbl", "datafield",
            "header", "headerl", "headerr", "headerf", "footer", "footerl", "footerr", "footerf",
            "bkmkstart", "bkmkend", "nonshppict", "shppict", "blipuid", "panose", "falt", "listtext",
        ];

        private record State(bool Skip, int UnicodeSkip);

        public static string Extract(string rtf, int limit)
        {
            var text = new StringBuilder();
            var pendingBytes = new List<byte>();
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
            var stack = new Stack<State>();
            var state = new State(false, 1);
            var skipChars = 0;

            void Flush()
            {
                if (pendingBytes.Count == 0) return;
                if (!state.Skip) text.Append(encoding.GetString([.. pendingBytes]));
                pendingBytes.Clear();
            }

            void Emit(string s)
            {
                Flush();
                if (!state.Skip) text.Append(s);
            }

            var i = 0;
            while (i < rtf.Length && text.Length < limit)
            {
                var c = rtf[i];
                if (c == '{')
                {
                    Flush();
                    stack.Push(state);
                    i++;
                }
                else if (c == '}')
                {
                    Flush();
                    state = stack.Count > 0 ? stack.Pop() : state;
                    i++;
                }
                else if (c == '\\')
                {
                    i++;
                    if (i >= rtf.Length) break;
                    var next = rtf[i];

                    if (next == '\'' && i + 2 < rtf.Length)
                    {
                        var hex = rtf.Substring(i + 1, 2);
                        i += 3;
                        if (skipChars > 0) { skipChars--; continue; }
                        if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var b)) pendingBytes.Add(b);
                        continue;
                    }
                    if (!char.IsAsciiLetter(next))
                    {
                        i++;
                        switch (next)
                        {
                            case '*': Flush(); state = state with { Skip = true }; break;
                            case '~': Emit(" "); break;
                            case '_': Emit("-"); break;
                            case '\\' or '{' or '}': Emit(next.ToString()); break;
                            case '\r' or '\n': Emit("\n"); break;
                        }
                        continue;
                    }

                    var start = i;
                    while (i < rtf.Length && char.IsAsciiLetter(rtf[i])) i++;
                    var word = rtf[start..i];
                    var numberStart = i;
                    if (i < rtf.Length && rtf[i] == '-') i++;
                    while (i < rtf.Length && char.IsAsciiDigit(rtf[i])) i++;
                    int? number = i > numberStart && int.TryParse(rtf.AsSpan(numberStart, i - numberStart), out var n) ? n : null;
                    if (i < rtf.Length && rtf[i] == ' ') i++;

                    switch (word)
                    {
                        case "par" or "line" or "row" or "sect" or "page": Emit("\n"); break;
                        case "tab" or "cell": Emit("\t"); break;
                        case "emdash": Emit("—"); break;
                        case "endash": Emit("–"); break;
                        case "bullet": Emit("•"); break;
                        case "lquote": Emit("‘"); break;
                        case "rquote": Emit("’"); break;
                        case "ldblquote": Emit("“"); break;
                        case "rdblquote": Emit("”"); break;
                        case "ansicpg" when number is { } cp:
                            encoding = CodePagesEncodingProvider.Instance.GetEncoding(cp) ?? encoding;
                            break;
                        case "uc" when number is { } count:
                            state = state with { UnicodeSkip = count };
                            break;
                        case "u" when number is { } code:
                            Emit(((char)(code < 0 ? code + 65536 : code)).ToString());
                            skipChars = state.UnicodeSkip;
                            break;
                        default:
                            if (Skipped.Contains(word))
                            {
                                Flush();
                                state = state with { Skip = true };
                            }
                            break;
                    }
                }
                else
                {
                    i++;
                    if (c is '\r' or '\n') continue;
                    if (skipChars > 0) { skipChars--; continue; }
                    if (c < 0x80) Emit(c.ToString());
                    else pendingBytes.Add((byte)c);
                }
            }
            Flush();
            return text.ToString().Trim();
        }
    }
}
