using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Strip <cslist> <outDir>
//   Copies the cslist and every source it lists into outDir (same relative
//   paths) with all comments removed and blank runs collapsed. Each stripped
//   file is re-parsed and must be free of syntax errors, or the run fails.
sealed class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("usage: Strip <cslist> <outDir>"); return 2; }
        string cslist = Path.GetFullPath(args[0]);
        string root = Path.GetDirectoryName(cslist)!;
        string outDir = Path.GetFullPath(args[1]);
        var files = File.ReadAllLines(cslist).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var parse = new CSharpParseOptions(LanguageVersion.CSharp5);
        int inBytes = 0, outBytes = 0, problems = 0;
        foreach (string rel in files)
        {
            string src = Path.Combine(root, rel);
            if (!File.Exists(src)) { Console.Error.WriteLine("cslist entry missing: " + rel); problems++; continue; }
            string text = File.ReadAllText(src);
            inBytes += text.Length;
            var tree = CSharpSyntaxTree.ParseText(text, parse, src);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0) { Console.Error.WriteLine(rel + ": " + errors[0]); problems++; continue; }
            var stripped = new CommentRemover().Visit(tree.GetRoot());
            string result = Tidy(stripped.ToFullString());
            var check = CSharpSyntaxTree.ParseText(result, parse, src);
            var after = check.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (after.Count > 0) { Console.Error.WriteLine(rel + " (after strip): " + after[0]); problems++; continue; }
            string dst = Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst, result, new UTF8Encoding(false));
            outBytes += result.Length;
        }
        if (problems > 0) return 1;
        File.Copy(cslist, Path.Combine(outDir, Path.GetFileName(cslist)), true);
        Console.WriteLine("strip: " + files.Count + " files, " + inBytes + " -> " + outBytes + " chars");
        return 0;
    }

    // Trailing whitespace off every line, runs of blank lines down to one,
    // no leading blank lines, one trailing newline.
    static string Tidy(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        bool lastBlank = true;
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            bool blank = line.Length == 0;
            if (blank && lastBlank) continue;
            sb.Append(line).Append('\n');
            lastBlank = blank;
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }
}

sealed class CommentRemover : CSharpSyntaxRewriter
{
    public CommentRemover() : base(visitIntoStructuredTrivia: false) { }

    public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
    {
        switch (trivia.Kind())
        {
            case SyntaxKind.SingleLineCommentTrivia:
            case SyntaxKind.MultiLineCommentTrivia:
            case SyntaxKind.SingleLineDocumentationCommentTrivia:
            case SyntaxKind.MultiLineDocumentationCommentTrivia:
                return default;
            default:
                return base.VisitTrivia(trivia);
        }
    }
}
