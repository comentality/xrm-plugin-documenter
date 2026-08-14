using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PluginDocumenter
{
    /// <summary>
    /// Minimal C# colouriser for the preview pane. Everything the pane ever shows is this tool's
    /// own output — a few hundred lines at most — so a single regex pass over the whole buffer is
    /// enough, and it keeps the plugin a single dependency-free assembly. XrmToolBox does ship
    /// ScintillaNET, but without the native SciLexer.dll beside it, and the nuspec ships only
    /// PluginDocumenter.dll.
    /// </summary>
    internal static class CsSyntaxHighlighter
    {
        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // Visual Studio's default light theme, so the preview reads the way the file will once
        // it has been written.
        private static readonly Color CommentColor = Color.FromArgb(0, 128, 0);
        private static readonly Color DocTagColor = Color.FromArgb(128, 128, 128);
        private static readonly Color StringColor = Color.FromArgb(163, 21, 21);
        private static readonly Color KeywordColor = Color.FromArgb(0, 0, 255);
        private static readonly Color NumberColor = Color.FromArgb(9, 134, 88);
        private static readonly Color TypeColor = Color.FromArgb(43, 145, 175);
        private static readonly Color CallColor = Color.FromArgb(121, 94, 38);
        private static readonly Color DefaultColor = Color.Black;

        // Order is the precedence rule. Comments and strings come first so that a keyword quoted
        // inside one stays plain, and keywords come before types so that the 'assembly' of
        // [assembly: PluginAssembly(...)] is read as the contextual keyword it is rather than as
        // the attribute's name.
        private static readonly Regex Tokens = new Regex(
            @"(?<doc>///[^\r\n]*)" +
            @"|(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)" +
            @"|(?<string>@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\])*')" +
            @"|(?<keyword>\b(?:abstract|as|base|bool|break|byte|case|catch|char|checked|class|" +
            @"const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|" +
            @"false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|" +
            @"is|lock|long|namespace|new|null|object|operator|out|override|params|private|" +
            @"protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|" +
            @"string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|" +
            @"ushort|using|virtual|void|volatile|while|var|partial|nameof|assembly|get|set)\b)" +
            @"|(?<type>(?<=\[(?:\s*assembly\s*:)?\s*)[A-Za-z_]\w*" +
            @"|(?<=\b(?:class|enum|struct|interface)\s+)[A-Za-z_]\w*" +
            @"|\b[A-Z]\w*(?=\s*\.)" +
            @"|\b[A-Z]\w*(?=\??\s+[A-Za-z_]\w*))" +
            @"|(?<number>\b\d+(?:\.\d+)?\b)" +
            @"|(?<call>\b[A-Za-z_]\w*(?=\s*\())",
            RegexOptions.Compiled);

        /// <summary>The markup inside a /// line, which Visual Studio greys out against its text.</summary>
        private static readonly Regex DocTags = new Regex(@"</?[A-Za-z][^>]*>|///", RegexOptions.Compiled);

        public static void Apply(RichTextBox box, string code)
        {
            SendMessage(box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                box.Clear();
                box.Text = code ?? "";
                box.SelectAll();
                box.SelectionColor = DefaultColor;

                foreach (Match match in Tokens.Matches(box.Text))
                {
                    var color = ColorFor(match);
                    if (!color.HasValue) continue;

                    box.Select(match.Index, match.Length);
                    box.SelectionColor = color.Value;

                    if (match.Groups["doc"].Success)
                    {
                        foreach (Match tag in DocTags.Matches(match.Value))
                        {
                            box.Select(match.Index + tag.Index, tag.Length);
                            box.SelectionColor = DocTagColor;
                        }
                    }
                }

                box.Select(0, 0);
            }
            finally
            {
                SendMessage(box.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                box.Invalidate();
            }
        }

        /// <summary>Shows text that is not code, without any of it being mistaken for code.</summary>
        public static void Plain(RichTextBox box, string text)
        {
            box.Clear();
            box.Text = text ?? "";
            box.SelectAll();
            box.SelectionColor = DefaultColor;
            box.Select(0, 0);
        }

        private static Color? ColorFor(Match match)
        {
            if (match.Groups["doc"].Success) return CommentColor;
            if (match.Groups["comment"].Success) return CommentColor;
            if (match.Groups["string"].Success) return StringColor;
            if (match.Groups["keyword"].Success) return KeywordColor;
            if (match.Groups["type"].Success) return TypeColor;
            if (match.Groups["number"].Success) return NumberColor;
            if (match.Groups["call"].Success) return CallColor;
            return null;
        }
    }
}
