#:property TargetFramework=net10.0
#:property LangVersion=latest
#:property ImplicitUsings=enable
#:property Nullable=enable
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property TreatWarningsAsErrors=false
#:property CodeAnalysisTreatWarningsAsErrors=false
#:property EnforceCodeStyleInBuild=false
#:property AnalysisMode=Default
#:property GenerateDocumentationFile=false
// The properties above hold this app's build as it is, whatever Directory.Build.props takes on for the library: the
// hooks build and run this file on every commit and push, so warnings as errors, the code style in the build, another
// language version, implicit usings turned off or a lock file there would stop every commit, or leave a file in eng/.
//
// The gate: the checks a change passes before it lands. People run it, and the git hooks in .githooks/ call it.
//
// Usage: dotnet run eng/gate.cs -- quick|full [--base <ref>] [--head <ref>]
//        dotnet run eng/gate.cs -- text --file <path>|-
//        dotnet run eng/gate.cs -- selftest
//        dotnet run eng/gate.cs -- setup
//        dotnet run eng/gate.cs -- link [skill...]
//        dotnet run eng/gate.cs -- commit-msg|pre-push|post-checkout|post-merge [git's arguments]   (the hooks call these)
// The `--` is needed: without it `dotnet run` takes `--file` as its own option.
//
// quick and full check two things:
// - the working tree: the library builds, AGENTS.md is within 150 lines, each skill's frontmatter and its Codex link,
//   the hooks stored as executable and turned on in this clone; full adds the offline tests. These run when the head is
//   the checked-out commit, and are skipped, with the reason, when the tree is not what is checked;
// - the commits: no tool attribution in any message, author or committer, and gitleaks, on base..head (merges
//   included).
// Both start with the selftest, the gate's own tests. The gate prints a table and exits 1 when a check failed; a
// warning or a skip never fails it. Without gitleaks the secret scan is a warning, unless GATE_REQUIRE_GITLEAKS=1.
//
// text checks a PR's title and description, which a squash merge makes the commit on main: no tool attribution, as
// commit-msg checks a message, and a `Refs #<n>`, `Closes #<n>` or `Fixes #<n>` line. `-` reads standard input.
//
// setup, once per clone, turns the hooks on, points `git blame` at .git-blame-ignore-revs when there is one, and checks
// the skill links out as symlinks where this machine can make them. link writes .agents/skills/<skill> into git's
// index as a link to .claude/skills/<skill>.
//
// The attribution rule and its test cases come from omniverse-mcp's tools/gate.py, line for line. Its patterns are
// Python's: Py gives them Python's meaning, since .NET reads \w, \s, \b, IGNORECASE, line breaks and lower() differently,
// and the selftest has a case for each place they differ.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

return Cli.Run(args, Console.Out, Console.Error, Console.OpenStandardInput);

/// <summary>The repo the gate checks, and the paths and modes its checks read.</summary>
internal static class Repo
{
    public const string Base = "origin/main";
    public const int AgentsMaxLines = 150;
    public const string SkillsDir = ".claude/skills";
    public const string SkillLinksDir = ".agents/skills";
    public const string LinkMode = "120000";
    public const string HooksDir = ".githooks";
    public const string HookMode = "100755";
    public const string GitleaksConfig = ".gitleaks.toml";
    public const string GitleaksIgnore = ".gitleaksignore";
    public const string BlameIgnoreRevs = ".git-blame-ignore-revs";
    public const string Library = "Tableau.Api/Tableau.Api.csproj";

    /// <summary>The folder above eng/, wherever the command runs from.</summary>
    public static string Root { get; } = Path.GetFullPath(Path.Combine(
        AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetDirectoryName(SourceFile())!, ".."));

    private static string SourceFile([CallerFilePath] string path = "") => path;
}

internal sealed record Done(int ExitCode, string Stdout, string Stderr);

/// <summary>Processes with a fixed argument list and no shell, their output read as UTF-8.</summary>
internal static class Proc
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // What tells git which repository to use. A hook may run with them set; a command for another folder (the
    // selftest's scratch repos) drops them, so it never touches the repo the hook runs for.
    private static readonly string[] RepoVariables =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_COMMON_DIR", "GIT_NAMESPACE", "GIT_PREFIX",
    ];

    public static Done Run(string exe, IEnumerable<string> args, string? cwd = null, string? stdin = null,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd ?? Repo.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        foreach (var (key, value) in env ?? new Dictionary<string, string?>())
        {
            if (value is null) info.Environment.Remove(key);
            else info.Environment[key] = value;
        }
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{exe} did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            if (stdin is not null) process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process exited without reading its input; its exit code says what happened.
        }
        process.WaitForExit();
        return new Done(process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>git in cwd (the repo when null); the caller reads the exit code and the output.</summary>
    public static Done Git(IEnumerable<string> args, string? cwd = null, string? stdin = null)
    {
        var elsewhere = cwd is not null && !SamePath(cwd, Repo.Root);
        var env = elsewhere ? RepoVariables.ToDictionary(name => name, _ => (string?)null) : null;
        return Run(Which("git") ?? "git", args, cwd, stdin, env);
    }

    public static bool SamePath(string a, string b) => string.Equals(
        Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The full path of a program on PATH, or null.</summary>
    public static string? Which(string name)
    {
        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var path = Path.Combine(dir.Trim('"'), name + extension);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }
}

/// <summary>
/// Python's meaning for the strings and patterns the attribution rule is written in. A pattern is given in Python's
/// syntax and runs on a shadow of the text: one char for each UTF-16 unit, an ASCII char as itself and any other as
/// its class. The patterns are ASCII, so the class is all they could tell from the char, and each class means what it
/// does in Python: \w a letter, a char with a numeric value, or _; \d a decimal digit; \s what str.isspace() takes.
/// IGNORECASE folds A-Z, and the four other chars Python matches with an ASCII letter, before the match. A group is cut
/// from the text itself, at the same place. Unicode's tables are .NET's, so a char newer than Python's Unicode version
/// can class otherwise.
/// </summary>
internal static class Py
{
    private const char Word = '\uE000', Digit = '\uE001', Space = '\uE002', Other = '\uE003';
    private const string W = @"A-Za-z0-9_\uE000\uE001";
    private const string D = @"0-9\uE001";
    private const string S = @"\t\n\x0B\f\r\x1C-\x1F \uE002";

    /// <summary>str.isspace() for one char: every whitespace char is in the BMP.</summary>
    public static bool IsSpace(char c) => c is (>= '\t' and <= '\r') or (>= '\x1C' and <= ' ') or '\x85' or '\xA0'
        or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000';

    /// <summary>str.strip().</summary>
    public static string Strip(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && IsSpace(text[start])) start++;
        while (end > start && IsSpace(text[end - 1])) end--;
        return text[start..end];
    }

    /// <summary>str.strip(chars).</summary>
    public static string Strip(string text, string chars) => text.Trim(chars.ToCharArray());

    /// <summary>str.rstrip().</summary>
    public static string RStrip(string text)
    {
        var end = text.Length;
        while (end > 0 && IsSpace(text[end - 1])) end--;
        return text[..end];
    }

    /// <summary>str.split(): the runs of non-space text.</summary>
    public static List<string> Split(string text)
    {
        var parts = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var space = i == text.Length || IsSpace(text[i]);
            if (!space && start < 0) start = i;
            else if (space && start >= 0)
            {
                parts.Add(text[start..i]);
                start = -1;
            }
        }
        return parts;
    }

    /// <summary>str.splitlines(): it breaks at \r\n and at ten single chars, \x1C to \x1E and U+2028 among them.</summary>
    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0, i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '\n' or '\r' or '\x0B' or '\x0C' or '\x1C' or '\x1D' or '\x1E' or '\x85' or '\u2028' or '\u2029')
            {
                lines.Add(text[start..i]);
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                start = i;
            }
            else i++;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    /// <summary>
    /// str.lower(), as far as comparing it with ASCII text can tell: A-Z, and the Kelvin sign, the one other char
    /// Python lowers to ASCII. Any other char stays as it is, so it stays unequal to ASCII text, as it is in Python
    /// (U+0130 lowers to i and a combining dot there, where .NET would give i alone).
    /// </summary>
    public static string Lower(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
            span[i] = source[i] switch { >= 'A' and <= 'Z' => (char)(source[i] | 0x20), '\u212A' => 'k', var c => c };
    });

    /// <summary>The shadow a pattern runs on: the same length as text, so a match's place is the place in text.</summary>
    public static string Shadow(string text, bool ignoreCase)
    {
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                chars[i] = chars[i + 1] = Class(char.ConvertToUtf32(c, text[i + 1]));
                i++;
            }
            else if (c < 0x80) chars[i] = ignoreCase && c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c;
            else if (ignoreCase && c is '\u0130' or '\u0131') chars[i] = 'i';
            else if (ignoreCase && c == '\u017F') chars[i] = 's';
            else if (ignoreCase && c == '\u212A') chars[i] = 'k';
            else chars[i] = Class(c);
        }
        return new string(chars);
    }

    private static char Class(int codePoint)
    {
        if (codePoint is >= 0xD800 and <= 0xDFFF) return Other;
        if (codePoint <= 0xFFFF && IsSpace((char)codePoint)) return Space;
        var rune = new Rune(codePoint);
        if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber) return Digit;
        return Rune.IsLetter(rune) || Rune.GetNumericValue(rune) != -1 ? Word : Other;
    }

    /// <summary>
    /// A Python pattern as .NET reads the shadow: \w, \d, \s and \S become their classes, \b a lookaround on \w, and
    /// (?P&lt;name&gt; a .NET group. Anything else Python and .NET read alike in these patterns; an escape of a letter
    /// it does not know stops it, so a new pattern can't quietly take .NET's meaning.
    /// </summary>
    public static string Translate(string pattern)
    {
        var result = new StringBuilder();
        var inClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                var next = pattern[++i];
                result.Append((next, inClass) switch
                {
                    ('w', false) => $"[{W}]",
                    ('w', true) => W,
                    ('d', false) => $"[{D}]",
                    ('d', true) => D,
                    ('s', false) => $"[{S}]",
                    ('s', true) => S,
                    ('S', false) => $"[^{S}]",
                    ('b', false) => $"(?:(?<=[{W}])(?![{W}])|(?<![{W}])(?=[{W}]))",
                    ('t' or 'n' or 'r' or 'f' or 'v', _) => "\\" + next,
                    (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9', _) =>
                        throw new NotSupportedException($"\\{next} has no Python meaning in Py.Translate: {pattern}"),
                    _ => "\\" + next,
                });
            }
            else if (!inClass && c == '[')
            {
                inClass = true;
                result.Append(c);
                if (i + 1 < pattern.Length && pattern[i + 1] == '^') result.Append(pattern[++i]);
            }
            else if (inClass && c == ']')
            {
                inClass = false;
                result.Append(c);
            }
            else if (!inClass && string.CompareOrdinal(pattern, i, "(?P<", 0, 4) == 0)
            {
                result.Append("(?<");
                i += 3;
            }
            else result.Append(c);
        }
        return result.ToString();
    }
}

/// <summary>A Python pattern, compiled once: re.compile(pattern, re.IGNORECASE | re.MULTILINE as asked).</summary>
internal sealed class PyRegex(string pattern, bool ignoreCase = false, bool multiline = false)
{
    private readonly Regex _regex = new(Py.Translate(pattern),
        RegexOptions.CultureInvariant | (multiline ? RegexOptions.Multiline : RegexOptions.None));

    /// <summary>The pattern as Python reads it.</summary>
    public string Pattern { get; } = pattern;

    /// <summary>re.match: a match that starts at the text's start, or null.</summary>
    public PyMatch? Match(string text)
    {
        var match = _regex.Match(Py.Shadow(text, ignoreCase));
        return match.Success && match.Index == 0 ? new PyMatch(text, match) : null;
    }

    /// <summary>re.search: the first match anywhere, or null.</summary>
    public PyMatch? Search(string text)
    {
        var match = _regex.Match(Py.Shadow(text, ignoreCase));
        return match.Success ? new PyMatch(text, match) : null;
    }
}

internal sealed class PyMatch(string text, Match match)
{
    /// <summary>match.group(name): the text the group matched, or null when it took no part.</summary>
    public string? Group(string name) => Cut(match.Groups[name]);

    public string? Group(int number) => Cut(match.Groups[number]);

    private string? Cut(Group group) => group.Success ? text.Substring(group.Index, group.Length) : null;
}

/// <summary>The rules on commit messages and PR text, and the pure checks the gate runs on the tree.</summary>
internal static class Rules
{
    // Tool attribution: lines a tool adds to say it wrote a change. Nothing on `main` carries them (AGENTS.md, Commits
    // and PRs). Each pattern matches the line a tool adds, so a sentence about Claude, or a person named Claude, passes.
    // A line may sit behind symbols ("🤖 ", "- ", a comment character): git keeps comment lines in a message given with
    // -m, so the hook reads them too.
    private const string Tools =
        @"claude|anthropic|codex|openai|chatgpt|gpt-?\d\w*|copilot|gemini|devin|aider|windsurf|cursor\s*(?:agent|ai|ide)|"
        + @"cursoragent|cursor\.com";

    private static readonly PyRegex CoAuthor = new(@"^[^\w]*co-authored-by\s*:\s*(?P<who>.*?)\s*$", ignoreCase: true);
    private static readonly PyRegex Who = new(@"^(?P<name>[^<>]*?)\s*(?:<(?P<email>[^<>]*)>)?$");

    // A co-author is a tool when its address is a tool's, or its name is a tool's alone or with a model and a version
    // ("Claude", "Claude Opus 4.6", "GitHub Copilot", "aider (gpt-4o)"); "Claude Martin" is a person.
    private static readonly PyRegex ToolEmail = new(
        @"^(?:noreply@anthropic\.com|no-?reply@openai\.com|cursoragent@cursor\.com|noreply@aider\.chat"
        + @"|\d+\+copilot@users\.noreply\.github\.com)$",
        ignoreCase: true);

    // Any GitHub bot address is a tool, unless the trailer is one of the bots GitHub names a co-author when a person
    // pushes onto their PR, under that bot's own name. An allowlist matched exactly on name and address: a new AI bot,
    // or a tool's name with an allowed bot's address, is refused without anyone adding it here.
    private static readonly PyRegex BotEmail = new(
        @"^(?:\d+\+)?(?P<login>[^@\s]*)\[bot\]@users\.noreply\.github\.com$", ignoreCase: true);

    private static readonly HashSet<string> NonAiBots = new(["dependabot", "github-actions", "renovate"], StringComparer.Ordinal);
    private static readonly HashSet<string> NonAiBotNames = new(NonAiBots.Select(login => $"{login}[bot]"), StringComparer.Ordinal);

    // A bot's name with any other address, `claude[bot] <claude@example.org>`: refused by the same allowlist.
    private static readonly PyRegex BotName = new(@"^(?P<login>[^\s\[\]]+)\[bot\]$", ignoreCase: true);

    // The line a PR's description names its issue with; a squash merge keeps it in the commit on main (AGENTS.md,
    // Commits and PRs). A bare one is the PR template's last line left unfilled.
    private static readonly PyRegex IssueRef = new(@"^[^\w]*(?:refs|closes|fixes)\s+#\d+\b", ignoreCase: true, multiline: true);
    private static readonly PyRegex BareIssueRef = new(@"^[^\w]*(?:refs|closes|fixes)\s+#\s*$", ignoreCase: true, multiline: true);

    private static readonly PyRegex ToolName = new(
        @"^(?:claude(?:\s+code)?|anthropic|(?:openai\s+)?codex|chatgpt|gpt-?\d[\w.]*|(?:github\s+)?copilot"
        + @"|gemini(?:\s+code\s+assist)?|cursor(?:\s+agent)?|devin(?:\s+ai)?|aider|windsurf)"
        + @"(?:\s+(?:opus|sonnet|haiku|code|cli|pro|flash|ultra|nano|mini|turbo|max|agent|ai|assist|preview|latest"
        + @"|thinking|v?\d[\w.]*|\([^()]*\)))*$",
        ignoreCase: true);

    private static readonly PyRegex Generated = new(
        @"^[^\w]*(?:generated|created|written|authored|produced|assisted)\s+(?:with|by|using|via|in)\b.*?\b(?:"
        + Tools + @")\b",
        ignoreCase: true);

    private static readonly PyRegex SessionLink = new(
        @"(?:https?://)?(?:www\.)?(?:claude\.ai/(?:code|chat|share)\b|chatgpt\.com/(?:codex|share|c)/)", ignoreCase: true);

    private static readonly PyRegex ToolTrailer = new(
        @"^[^\w]*(?:claude|anthropic|codex|openai|chatgpt|copilot|gemini|cursor)-[\w-]+\s*:", ignoreCase: true);

    private static readonly PyRegex PlainYamlBreakers = new(@":(?:\s|$)|\s#");
    private static readonly PyRegex KeyLine = new(@"^([A-Za-z][\w-]*):(?:[ \t]+(.*))?$");
    private static readonly PyRegex SkillName = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$");
    private static readonly string[] SkillKeys = ["name", "description"];
    private static readonly string[] BlockStyles = [">", ">-", ">+", "|", "|-", "|+"];
    private const int SkillNameMax = 64;
    private const int DescriptionMax = 1024;

    /// <summary>tool_co_author: true when a Co-authored-by trailer's `Name &lt;email&gt;` is a tool, by its address or its name.</summary>
    public static bool ToolCoAuthor(string who)
    {
        var match = Who.Match(Py.Strip(who));
        if (match is null) return false;
        var email = Py.Strip(match.Group("email") ?? "");
        var name = Py.Strip(match.Group("name") ?? "");
        var bot = BotEmail.Match(email);
        if (bot is not null)
            return !(NonAiBots.Contains(Py.Lower(bot.Group("login")!)) && NonAiBotNames.Contains(Py.Lower(name)));
        var namedBot = BotName.Match(name);
        if (namedBot is not null && !NonAiBots.Contains(Py.Lower(namedBot.Group("login")!))) return true;
        return ToolEmail.Match(email) is not null || ToolName.Match(name) is not null;
    }

    /// <summary>
    /// attribution_lines: the lines of a commit message, PR title or body that carry tool attribution: a co-author
    /// that is a tool, a "Generated with" line, a tool's own trailer (`Claude-Session:`), a session link.
    /// </summary>
    public static List<string> AttributionLines(string message)
    {
        var found = new List<string>();
        foreach (var line in Py.SplitLines(message))
        {
            var coAuthor = CoAuthor.Match(line);
            if ((coAuthor is not null && ToolCoAuthor(coAuthor.Group("who")!))
                || Generated.Search(line) is not null
                || ToolTrailer.Search(line) is not null
                || SessionLink.Search(line) is not null)
                found.Add(Py.Strip(line));
        }
        return found;
    }

    /// <summary>What is wrong with how a PR's description names its issue: it needs a Refs, Closes or Fixes #n line.</summary>
    public static List<string> IssueRefProblems(string text)
    {
        if (IssueRef.Search(text) is not null) return [];
        if (BareIssueRef.Search(text) is not null)
            return ["a `Refs #` or `Closes #` line has no issue number: write `Closes #<n>`, or `Refs #<n>` for one step"];
        return ["no line names the issue: end the description with `Closes #<n>`, or `Refs #<n>` for one step of it"];
    }

    /// <summary>One line per attribution line in each (sha, message).</summary>
    public static List<string> CommitProblems(IEnumerable<(string Sha, string Message)> commits) =>
    [
        .. from commit in commits
           from line in AttributionLines(commit.Message)
           select $"{commit.Sha[..Math.Min(10, commit.Sha.Length)]}: {line}",
    ];

    // A tool's name alone, which a person may also be called.
    private static readonly PyRegex BareToolName = new(
        @"^(?:claude|anthropic|codex|chatgpt|copilot|gemini|cursor|devin|aider|windsurf)$", ignoreCase: true);

    /// <summary>
    /// True when a commit's author or committer, `Name &lt;email&gt;`, is a tool. The address decides, as for a
    /// co-author: a tool's address, any bot's but the allowed ones, a bot's name. The name decides only when it says
    /// more than a first name could ("Claude Code", "GitHub Copilot", a model, a version): a commit's identity is
    /// someone's own, and Claude Dupont may commit as Claude, where a co-author trailer naming Claude alone is a tool.
    /// </summary>
    public static bool ToolIdentity(string who)
    {
        var match = Who.Match(Py.Strip(who));
        if (match is null) return false;
        var email = Py.Strip(match.Group("email") ?? "");
        var name = Py.Strip(match.Group("name") ?? "");
        var bot = BotEmail.Match(email);
        if (bot is not null)
            return !(NonAiBots.Contains(Py.Lower(bot.Group("login")!)) && NonAiBotNames.Contains(Py.Lower(name)));
        var namedBot = BotName.Match(name);
        if (namedBot is not null && !NonAiBots.Contains(Py.Lower(namedBot.Group("login")!))) return true;
        return ToolEmail.Match(email) is not null || (ToolName.Match(name) is not null && BareToolName.Match(name) is null);
    }

    /// <summary>One line per commit whose author or committer is a tool: a commit a tool made under its own name carries no trailer.</summary>
    public static List<string> IdentityProblems(IEnumerable<Commit> commits) =>
    [
        .. from commit in commits
           from person in new[] { ("author", commit.Author), ("committer", commit.Committer) }
           where ToolIdentity(person.Item2)
           select $"{commit.Sha[..Math.Min(10, commit.Sha.Length)]}: the {person.Item1} is a tool: {person.Item2}",
    ];

    /// <summary>The commits in `git log --format=%H%x00%an &lt;%ae&gt;%x00%cn &lt;%ce&gt;%x00%B%x01` (LogFormat).</summary>
    public static List<Commit> ParseLog(string output)
    {
        var commits = new List<Commit>();
        foreach (var record in output.Split('\x01'))
        {
            var fields = Py.Strip(record, "\r\n").Split('\0', 4);
            if (fields.Length == 4) commits.Add(new Commit(Py.Strip(fields[0]), fields[1], fields[2], Py.Strip(fields[3])));
        }
        return commits;
    }

    public const string LogFormat = "--format=%H%x00%an <%ae>%x00%cn <%ce>%x00%B%x01";

    private static readonly Regex GitleaksError = new(@"^\S+ (?:ERR|FTL) ", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>
    /// Why a gitleaks run that exited 0 can't be trusted, or null: when git log fails under it (a blob it can't fetch,
    /// a range it can't read), gitleaks logs the error, scans nothing and still exits 0. Its count of commits scanned
    /// says nothing: it leaves out a commit that only deletes, renames, changes a mode or adds an empty file.
    /// </summary>
    public static string? GitleaksProblem(string output)
    {
        var error = GitleaksError.Match(output);
        return error.Success
            ? $"gitleaks logged an error, so the scan can't be trusted: {Py.Strip(output[error.Index..].Split('\n')[0])}"
            : null;
    }

    /// <summary>AGENTS.md stays within its line limit: every agent reads all of it at the start of each session.</summary>
    public static List<string> AgentsMdProblems(string text, int limit = Repo.AgentsMaxLines)
    {
        var lines = Py.SplitLines(text).Count;
        return lines > limit ? [$"AGENTS.md has {lines} lines; keep it within {limit}"] : [];
    }

    /// <summary>
    /// The `key: value` pairs between a file's opening `---` and the next, each value with whether it is a plain
    /// scalar (true) or quoted or a block (`>-`, `|`). Block and continuation lines are joined with spaces. Enough YAML
    /// for a skill's frontmatter, which holds two keys; throws FormatException on anything else.
    /// </summary>
    public static OrderedDictionary<string, (string Value, bool Plain)> Frontmatter(string text)
    {
        var lines = Py.SplitLines(text);
        if (lines.Count == 0 || Py.RStrip(lines[0]) != "---") throw new FormatException("it does not start with a --- line");
        var end = Enumerable.Range(1, Math.Max(0, lines.Count - 1)).Cast<int?>().FirstOrDefault(i => Py.RStrip(lines[i!.Value]) == "---")
            ?? throw new FormatException("its frontmatter has no closing --- line");
        var fields = new OrderedDictionary<string, (string, bool)>(StringComparer.Ordinal);
        string? key = null;
        var parts = new List<string>();
        var plain = true;

        void Close()
        {
            if (key is not null) fields[key] = (string.Join(" ", parts.Where(part => part.Length > 0)), plain);
        }

        foreach (var line in lines.Skip(1).Take(end - 1))
        {
            if (key is not null && (Py.Strip(line).Length == 0 || line.StartsWith(' ') || line.StartsWith('\t')))
            {
                parts.Add(Py.Strip(line));
                continue;
            }
            Close();
            key = null;
            parts = [];
            plain = true;
            if (Py.Strip(line).Length == 0 || line.StartsWith('#')) continue;
            var match = KeyLine.Match(line) ?? throw new FormatException($"the line {Repr(Py.Strip(line))} is not `key: value`");
            key = match.Group(1)!;
            var value = Py.Strip(match.Group(2) ?? "");
            if (fields.ContainsKey(key)) throw new FormatException($"{key} is set twice");
            if (BlockStyles.Contains(value)) plain = false;
            else if (value.StartsWith('\'') || value.StartsWith('"'))
            {
                if (value.Length < 2 || value[^1] != value[0])
                    throw new FormatException($"{key}'s quoted value does not end with its quote on the same line");
                parts = [value[1..^1]];
                plain = false;
            }
            else parts = [value];
        }
        Close();
        return fields;
    }

    /// <summary>
    /// What is wrong with `.claude/skills/&lt;folder&gt;/SKILL.md` for Claude Code and Codex: the frontmatter holds only
    /// `name` (equal to the folder) and `description` ("Use when ...", within 1,024 characters, valid YAML).
    /// </summary>
    public static List<string> SkillProblems(string folder, string text)
    {
        var where = $"{Repo.SkillsDir}/{folder}/SKILL.md";
        OrderedDictionary<string, (string Value, bool Plain)> fields;
        try
        {
            fields = Frontmatter(text);
        }
        catch (FormatException ex)
        {
            return [$"{where}: {ex.Message}"];
        }
        var problems = fields.Keys.Where(key => !SkillKeys.Contains(key))
            .Select(key => $"{where}: frontmatter key {Repr(key)}: a skill's frontmatter holds only name and description (write-skill)")
            .ToList();
        var name = fields.TryGetValue("name", out var named) ? named.Value : "";
        if (name != folder) problems.Add($"{where}: name is {Repr(name)}; it must equal the folder name {Repr(folder)}");
        if (SkillName.Match(folder) is null || Length(folder) > SkillNameMax)
            problems.Add($"{where}: the folder name takes lowercase letters, digits and hyphens, at most 64");
        var (description, plain) = fields.TryGetValue("description", out var described) ? described : ("", true);
        if (description.Length == 0)
        {
            problems.Add($"{where}: description is missing; agents pick a skill by it");
            return problems;
        }
        if (!description.StartsWith("Use when", StringComparison.Ordinal))
            problems.Add($"{where}: description starts with its trigger, \"Use when ...\"");
        if (Length(description) > DescriptionMax)
            problems.Add($"{where}: description has {Length(description)} characters; keep it within {DescriptionMax}");
        if (plain && PlainYamlBreakers.Search(description) is not null)
            problems.Add($"{where}: description: plain YAML can't hold a colon or a #: write it as a >- block");
        return problems;
    }

    /// <summary>
    /// Each skill has one link `.agents/skills/&lt;name&gt;`, a symlink in git's index (mode 120000) whose target is
    /// `../../.claude/skills/&lt;name&gt;`, and each link leads to a skill. entries: (mode, path, the blob's text).
    /// </summary>
    public static List<string> LinkProblems(IEnumerable<(string Mode, string Path, string Target)> entries, ISet<string> skills)
    {
        var problems = new List<string>();
        var linked = new HashSet<string>(StringComparer.Ordinal);
        const string Fix = "dotnet run eng/gate.cs -- link";
        foreach (var (mode, path, target) in entries)
        {
            var rest = path[(Repo.SkillLinksDir.Length + 1)..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            var name = slash < 0 ? rest : rest[..slash];
            if (slash >= 0 && rest.Length > slash + 1)
            {
                if (!linked.Contains(name))
                    problems.Add($"{Repo.SkillLinksDir}/{name} holds files; it must be one link: delete it, then {Fix} {name}");
                linked.Add(name);
                continue;
            }
            linked.Add(name);
            if (!skills.Contains(name)) problems.Add($"{path} leads to no skill in {Repo.SkillsDir}: delete it");
            else if (mode != Repo.LinkMode) problems.Add($"{path} is a plain file in git (mode {mode}), not a link: {Fix} {name}");
            else if (Py.Strip(target) != $"../../{Repo.SkillsDir}/{name}")
                problems.Add($"{path} points to {Repr(Py.Strip(target))}, not ../../{Repo.SkillsDir}/{name}: {Fix} {name}");
        }
        problems.AddRange(skills.Where(name => !linked.Contains(name)).Order(StringComparer.Ordinal)
            .Select(name => $"{Repo.SkillLinksDir}/{name} is missing, so Codex can't see the skill: {Fix} {name}"));
        return problems;
    }

    /// <summary>
    /// Each file in .githooks/ is stored as executable (mode 100755): Linux and macOS skip a hook without the bit, and
    /// a hook added from Windows gets 100644. entries: (mode, path).
    /// </summary>
    public static List<string> HookProblems(IEnumerable<(string Mode, string Path)> entries) =>
    [
        .. entries.Where(entry => entry.Mode != Repo.HookMode).Select(entry =>
            $"{entry.Path} is stored as mode {entry.Mode}, so Linux and macOS skip it: git update-index --chmod=+x {entry.Path}"),
    ];

    /// <summary>True for an environment value such as 1, true or yes.</summary>
    public static bool Truthy(string? value) => Py.Strip(value ?? "").ToLowerInvariant() is not ("" or "0" or "false" or "no" or "off");

    /// <summary>The last non-empty line of a tool's output, without ===== framing: the table's detail.</summary>
    public static string LastLine(string text) =>
        Py.SplitLines(text).Where(line => Py.Strip(line).Length > 0).Select(line => Py.Strip(Py.Strip(line).Trim('='))).LastOrDefault() ?? "";

    /// <summary>A string as Python's repr() shows it in a message.</summary>
    public static string Repr(string text) => $"'{text}'";

    private static int Length(string text) => text.EnumerateRunes().Count();
}

/// <summary>A commit as the gate checks it: its id, `Name &lt;email&gt;` of its author and its committer, its message.</summary>
internal sealed record Commit(string Sha, string Author, string Committer, string Message);

internal sealed class Result(string name, string status, string detail = "", string output = "")
{
    public string Name { get; } = name;
    public string Status { get; } = status;
    public string Detail { get; } = detail;
    public string Output { get; } = output;
    public double? Seconds { get; set; }
}

/// <summary>What one run checks: the commits in its ranges, listed once, and whether the working tree is what they hold.</summary>
internal sealed class Scope(List<string> ranges, string? treeSkipped, List<Commit> commits, string? problem)
{
    public List<string> Ranges { get; } = ranges;
    public string? TreeSkipped { get; set; } = treeSkipped;
    public List<Commit> Commits { get; } = commits;
    public string? Problem { get; } = problem;

    public static Scope Of(List<string> ranges, string? treeSkipped = null, string? cwd = null)
    {
        var (commits, problem) = Lanes.ListCommits(ranges, cwd);
        return new Scope(ranges, treeSkipped, commits, problem);
    }
}

/// <summary>The lanes: which checks run, in what order, and the table they print.</summary>
internal static class Lanes
{
    // The git log options every scan starts with. gitleaks reads `git log -p` as git prints it, and a developer's git
    // config can reshape that: colour codes leave it reading no commit, a short commit id or a missing path prefix loses
    // each finding's commit or path, a textconv driver shows converted text. Options on the command line outrank every
    // setting. Without --diff-merges git shows a merge commit no diff at all, so what a merge's resolution adds would
    // never be scanned; against its first parent, it is.
    private const string GitleaksPlainLog = "--no-color --no-textconv --no-show-signature --pretty=medium --date=default "
        + "--no-abbrev-commit --src-prefix=a/ --dst-prefix=b/ --text --diff-merges=first-parent";

    private const int OutputTail = 40;
    private static readonly Regex FullSha = new(@"\b([0-9a-f]{10})[0-9a-f]{30}\b", RegexOptions.CultureInvariant);
    private static readonly Regex Warnings = new(@"(\d+) Warning\(s\)", RegexOptions.CultureInvariant);

    private static readonly (string Name, Func<Scope, Result> Check, bool ReadsTree)[] Quick =
    [
        ("selftest", CheckSelftest, false),
        ("build", CheckBuild, true),
        ("AGENTS.md", CheckAgentsMd, true),
        ("skills", CheckSkills, true),
        ("skill links", CheckSkillLinks, true),
        ("hooks", CheckHooks, true),
        ("attribution", CheckAttribution, false),
        ("gitleaks", CheckGitleaks, false),
    ];

    // What full adds to quick. The offline tests the modernization brings go here, each a check that reads the working
    // tree: `dotnet test` on them, then the coverage floor.
    private static readonly (string Name, Func<Scope, Result> Check, bool ReadsTree)[] FullOnly =
    [
        ("tests", CheckTests, true),
    ];

    /// <summary>Every commit in the ranges, merges included, each once; and why they could not be listed.</summary>
    public static (List<Commit> Commits, string? Problem) ListCommits(List<string> ranges, string? cwd = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var commits = new List<Commit>();
        foreach (var rev in ranges)
        {
            var done = Proc.Git(["log", Rules.LogFormat, rev], cwd);
            if (done.ExitCode != 0)
            {
                var reason = Py.SplitLines(Py.Strip(done.Stderr)).FirstOrDefault() ?? "git log failed";
                return ([], $"can't list {rev} ({reason}): {FetchHint(rev)}, then run it again");
            }
            foreach (var commit in Rules.ParseLog(done.Stdout))
                if (seen.Add(commit.Sha)) commits.Add(commit);
        }
        return (commits, null);
    }

    // A plain `git fetch origin` doesn't make origin/main in a clone that fetches one other branch, or none.
    private static string FetchHint(string rev) => rev.StartsWith($"{Repo.Base}..", StringComparison.Ordinal)
        ? "git fetch origin main:refs/remotes/origin/main"
        : "fetch the commits it names";

    /// <summary>
    /// A `from..to` range with both ends as full commit ids, or why it can't be. gitleaks splits its --log-opts at
    /// spaces, so a ref such as `HEAD@{1 day ago}` would reach git log as three words and scan nothing.
    /// </summary>
    public static (string? Range, string? Problem) ResolveRange(string rev, string? cwd = null)
    {
        var cut = rev.IndexOf("..", StringComparison.Ordinal);
        if (cut < 0) return (null, $"{rev} is no from..to range");
        var ends = new List<string>();
        foreach (var end in new[] { rev[..cut], rev[(cut + 2)..] })
        {
            // Resolved first, then peeled: `:/<text>` takes the rest of its argument as the text it looks for, so
            // `:/<text>^{commit}` would look for that suffix too.
            var resolved = Py.Strip(Proc.Git(["rev-parse", "--verify", "--quiet", end], cwd).Stdout);
            var sha = resolved.Length == 0 ? ""
                : Py.Strip(Proc.Git(["rev-parse", "--verify", "--quiet", $"{resolved}^{{commit}}"], cwd).Stdout);
            if (sha.Length != 40 && sha.Length != 64) return (null, $"can't resolve {end} in {rev} to a commit");
            ends.Add(sha);
        }
        return ($"{ends[0]}..{ends[1]}", null);
    }

    /// <summary>
    /// gitleaks on one range of the repo at root, with the config at config: null when it found nothing it could
    /// trust, or the failed result.
    /// </summary>
    public static Result? Gitleaks(string exe, string root, string config, string rev)
    {
        var (range, unresolved) = ResolveRange(rev, root);
        if (range is null) return new Result("gitleaks", "FAIL", unresolved!);
        var done = Proc.Run(exe,
        [
            "git", "--config", config, "--log-opts", $"{GitleaksPlainLog} {range}",
            "--redact", "--no-banner", "--no-color", "--ignore-gitleaks-allow", "--exit-code", "1", "-v", root,
        ], cwd: root);
        var output = Py.Strip(done.Stdout + done.Stderr);
        if (done.ExitCode != 0) return new Result("gitleaks", "FAIL", $"secrets found in {rev}, or the scan failed", output);
        return Rules.GitleaksProblem(output) is { } problem ? new Result("gitleaks", "FAIL", $"{rev}: {problem}", output) : null;
    }

    /// <summary>Each folder under .claude/skills that holds a SKILL.md, by name.</summary>
    public static List<(string Name, string Path)> SkillFolders(string root) =>
        !Directory.Exists(Path.Combine(root, Repo.SkillsDir))
            ? []
            : [.. Directory.GetDirectories(Path.Combine(root, Repo.SkillsDir))
                .Where(dir => File.Exists(Path.Combine(dir, "SKILL.md")))
                .Select(dir => (Name: Path.GetFileName(dir), Path: Path.Combine(dir, "SKILL.md")))
                .OrderBy(skill => skill.Name, StringComparer.Ordinal)];

    /// <summary>(mode, blob sha, path) of each entry in git's index under pathspec.</summary>
    public static List<(string Mode, string Sha, string Path)> IndexEntries(string pathspec, string? cwd = null)
    {
        var listed = Proc.Git(["ls-files", "-s", "--", pathspec], cwd);
        if (listed.ExitCode != 0) throw new IOException(Py.Strip(listed.Stderr) is { Length: > 0 } why ? why : $"git ls-files failed for {pathspec}");
        var entries = new List<(string, string, string)>();
        foreach (var line in Py.SplitLines(listed.Stdout))
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            var meta = Py.Split(line[..tab]);
            entries.Add((meta[0], meta[1], line[(tab + 1)..]));
        }
        return entries;
    }

    // The checks

    private static Result CheckSelftest(Scope _)
    {
        var (cases, failures, skipped) = SelfTest.Run();
        var also = skipped.Count > 0 ? $", {skipped.Count} skipped ({skipped[0][(skipped[0].IndexOf(": ", StringComparison.Ordinal) + 2)..]})" : "";
        return failures.Count == 0
            ? new Result("selftest", "pass", $"{cases} cases{also}")
            : new Result("selftest", "FAIL", $"{failures.Count} of {cases} cases failed{also}", string.Join("\n", failures));
    }

    private static Result CheckBuild(Scope _)
    {
        var dotnet = Proc.Which("dotnet");
        if (dotnet is null) return new Result("build", "FAIL", "dotnet not found: https://dotnet.microsoft.com/download");
        var done = Proc.Run(dotnet, ["build", Repo.Library, "-nologo", "-tl:off", "-nodeReuse:false"],
            env: new Dictionary<string, string?> { ["DOTNET_CLI_UI_LANGUAGE"] = "en" });
        var output = Py.Strip(done.Stdout + done.Stderr);
        if (done.ExitCode != 0) return new Result("build", "FAIL", Rules.LastLine(output), output);
        var warnings = Warnings.Match(output);
        return new Result("build", "pass", $"{Repo.Library}, {(warnings.Success ? warnings.Groups[1].Value : "?")} warnings");
    }

    private static Result CheckAgentsMd(Scope _)
    {
        var path = Path.Combine(Repo.Root, "AGENTS.md");
        if (!File.Exists(path)) return new Result("AGENTS.md", "FAIL", "AGENTS.md is missing");
        var text = ReadText(path, () => Stream.Null);
        var problems = Rules.AgentsMdProblems(text);
        return new Result("AGENTS.md", problems.Count > 0 ? "FAIL" : "pass",
            problems.Count > 0 ? problems[0] : $"{Py.SplitLines(text).Count} lines");
    }

    private static Result CheckSkills(Scope _)
    {
        var skills = SkillFolders(Repo.Root);
        var agentsPath = Path.Combine(Repo.Root, "AGENTS.md");
        var agents = File.Exists(agentsPath) ? ReadText(agentsPath, () => Stream.Null) : "";
        var problems = new List<string>();
        foreach (var (name, path) in skills)
        {
            problems.AddRange(Rules.SkillProblems(name, ReadText(path, () => Stream.Null)));
            if (!agents.Contains($"`{name}`", StringComparison.Ordinal))
                problems.Add($"AGENTS.md does not name the skill `{name}` (write-skill)");
        }
        return Outcome("skills", problems, $"{skills.Count} skills");
    }

    // Read from git's index, not the disk: where git makes no symlinks (Windows without Developer Mode) a link is a
    // plain file holding its target, and only the index says it is a link.
    private static Result CheckSkillLinks(Scope _)
    {
        var entries = IndexEntries(Repo.SkillLinksDir)
            .Select(entry => (entry.Mode, entry.Path,
                Target: entry.Mode == Repo.LinkMode ? Proc.Git(["cat-file", "blob", entry.Sha]).Stdout : ""))
            .ToList();
        var links = entries.Count(entry => !entry.Path[(Repo.SkillLinksDir.Length + 1)..].Contains('/', StringComparison.Ordinal));
        var skills = new HashSet<string>(SkillFolders(Repo.Root).Select(skill => skill.Name), StringComparer.Ordinal);
        return Outcome("skill links", Rules.LinkProblems(entries, skills), $"{links} links");
    }

    private static Result CheckHooks(Scope _)
    {
        var entries = IndexEntries(Repo.HooksDir).Select(entry => (entry.Mode, entry.Path)).ToList();
        return HooksResult(entries, Py.Strip(Proc.Git(["config", "--get", "core.hooksPath"]).Stdout), Repo.Root);
    }

    /// <summary>The hooks check on what git's index and config say: stored executable, and turned on in this clone.</summary>
    public static Result HooksResult(List<(string Mode, string Path)> entries, string hooksPath, string root)
    {
        if (entries.Count == 0) return new Result("hooks", "FAIL", $"nothing in {Repo.HooksDir} is in git's index");
        var problems = Rules.HookProblems(entries);
        if (problems.Count > 0) return Outcome("hooks", problems, "");
        // A clone that never ran setup commits without commit-msg; the branch's messages are still checked below.
        if (hooksPath.Length == 0 || !Proc.SamePath(Path.Combine(root, hooksPath), Path.Combine(root, Repo.HooksDir)))
            return new Result("hooks", "warn", $"{entries.Count} executable, but this clone doesn't run them: dotnet run eng/gate.cs -- setup");
        return new Result("hooks", "pass", $"{entries.Count} executable, turned on");
    }

    private static Result CheckAttribution(Scope scope)
    {
        if (scope.Problem is not null) return new Result("attribution", "FAIL", scope.Problem);
        var found = Rules.CommitProblems(scope.Commits.Select(commit => (commit.Sha, commit.Message)))
            .Concat(Rules.IdentityProblems(scope.Commits)).ToList();
        return found.Count > 0
            ? new Result("attribution", "FAIL", $"tool attribution in {found.Count} place(s)",
                string.Join("\n", found) + "\nReword each commit without it (git rebase -i, reword); a commit a tool authored or "
                + "committed takes a person's name (git commit --amend --reset-author).")
            : new Result("attribution", "pass", $"{scope.Commits.Count} commits");
    }

    private static Result CheckGitleaks(Scope scope)
    {
        var exe = Proc.Which("gitleaks");
        if (exe is null)
        {
            return Rules.Truthy(Environment.GetEnvironmentVariable("GATE_REQUIRE_GITLEAKS"))
                ? new Result("gitleaks", "FAIL", "not installed, and GATE_REQUIRE_GITLEAKS=1 requires it")
                : new Result("gitleaks", "warn", "not installed, so not scanned; https://github.com/gitleaks/gitleaks");
        }
        if (File.Exists(Path.Combine(Repo.Root, Repo.GitleaksIgnore)))
            return new Result("gitleaks", "FAIL", $"{Repo.GitleaksIgnore} skips findings without a reason",
                $"Delete {Repo.GitleaksIgnore}; allow each exception in {Repo.GitleaksConfig} with its reason.");
        if (scope.Problem is not null) return new Result("gitleaks", "FAIL", scope.Problem);
        if (scope.Commits.Count == 0) return new Result("gitleaks", "pass", "no commits to scan");
        foreach (var rev in scope.Ranges)
            if (Gitleaks(exe, Repo.Root, Path.Combine(Repo.Root, Repo.GitleaksConfig), rev) is { } failed) return failed;
        return new Result("gitleaks", "pass", $"{scope.Commits.Count} commits");
    }

    // The library has no offline tests yet: Tableau.Api.Tests runs only against a live site (AGENTS.md, Build and test).
    private static Result CheckTests(Scope _) =>
        new("tests", "pass", "no offline tests yet, so none ran; the live suite needs a Tableau site");

    private static Result Outcome(string name, List<string> problems, string detail) =>
        problems.Count > 0
            ? new Result(name, "FAIL", problems.Count == 1 ? problems[0] : $"{problems.Count} problems", string.Join("\n", problems))
            : new Result(name, "pass", detail);

    // Output

    private static string Row(string name, string status, string seconds, string detail, int width) =>
        $"  {name.PadRight(width)}  {status,-6}  {seconds,6}  {detail}".TrimEnd();

    /// <summary>Run the lane's checks on the scope, print the table, and return the exit code: 1 when any failed.</summary>
    public static int RunLane(string lane, Scope scope, TextWriter stdout)
    {
        var checks = lane == "full" ? [.. Quick, .. FullOnly] : Quick;
        var width = checks.Max(check => check.Name.Length);
        var ranges = string.Join(", ", scope.Ranges) is { Length: > 0 } joined ? FullSha.Replace(joined, "$1") : "(none)";
        stdout.WriteLine($"gate {lane}, commits {ranges}");
        if (scope.TreeSkipped is not null) stdout.WriteLine($"the working tree's checks are skipped: {scope.TreeSkipped}");
        stdout.WriteLine();
        stdout.WriteLine(Row("check", "result", "time", "detail", width));
        stdout.Flush();
        var results = new List<Result>();
        foreach (var (name, check, readsTree) in checks)
        {
            Result result;
            if (readsTree && scope.TreeSkipped is not null) result = new Result(name, "skip", "the working tree is not what is checked");
            else
            {
                var clock = Stopwatch.StartNew();
                try
                {
                    result = check(scope);
                }
                catch (Exception ex)
                {
                    result = new Result(name, "FAIL", $"{ex.GetType().Name}: {ex.Message}");
                }
                result.Seconds = clock.Elapsed.TotalSeconds;
            }
            results.Add(result);
            var seconds = result.Seconds is { } s ? string.Create(CultureInfo.InvariantCulture, $"{s:F1}s") : "";
            stdout.WriteLine(Row(name, result.Status, seconds, result.Detail, width));
            stdout.Flush();
        }
        var failed = results.Where(result => result.Status == "FAIL").ToList();
        foreach (var result in failed.Where(result => result.Output.Length > 0))
        {
            stdout.WriteLine();
            stdout.WriteLine($"--- {result.Name} ---");
            stdout.WriteLine(string.Join("\n", Py.SplitLines(result.Output).TakeLast(OutputTail)));
        }
        var warned = results.Count(result => result.Status == "warn");
        var skipped = results.Count(result => result.Status == "skip");
        var extra = (warned > 0 ? $", {warned} warning(s)" : "") + (skipped > 0 ? $", {skipped} skipped" : "");
        stdout.WriteLine();
        if (failed.Count > 0)
        {
            stdout.WriteLine($"gate {lane} FAILED: {string.Join(", ", failed.Select(result => result.Name))}");
            return 1;
        }
        stdout.WriteLine($"gate {lane} passed: {results.Count - warned - skipped} checks{extra}");
        return 0;
    }

    /// <summary>base..head; the working tree's checks run when head is the checked-out commit.</summary>
    public static Scope CliScope(string baseRef, string head)
    {
        var headSha = Py.Strip(Proc.Git(["rev-parse", "--verify", "--quiet", $"{head}^{{commit}}"]).Stdout);
        var checkedOut = Py.Strip(Proc.Git(["rev-parse", "--verify", "--quiet", "HEAD^{commit}"]).Stdout);
        var skipped = headSha.Length > 0 && headSha == checkedOut ? null : $"{head} is not the checked-out commit";
        return Scope.Of([$"{baseRef}..{head}"], skipped);
    }

    /// <summary>
    /// The commit-msg hook's check and the text lane's: `&lt;who&gt;: refused: ...` on stderr for each line of text that
    /// carries tool attribution and each of the caller's other problems, then the hint; 1 when there is any.
    /// </summary>
    public static int ReportAttribution(string text, string who, string hint, IEnumerable<string>? more, TextWriter stderr)
    {
        var problems = Rules.AttributionLines(text).Select(line => $"tool attribution: {line}").Concat(more ?? []).ToList();
        foreach (var problem in problems) stderr.WriteLine($"{who}: refused: {problem}");
        if (problems.Count > 0) stderr.WriteLine($"{who}: {hint}");
        stderr.Flush();
        return problems.Count > 0 ? 1 : 0;
    }

    /// <summary>The text lane: 1 when a PR's title and description carry tool attribution or name no issue.</summary>
    public static int TextLane(string text, TextWriter stdout, TextWriter stderr)
    {
        const string Hint = "fix the PR's title and description (AGENTS.md, Commits and PRs)";
        if (ReportAttribution(text, "gate text", Hint, Rules.IssueRefProblems(text), stderr) != 0)
        {
            stdout.WriteLine("gate text FAILED");
            return 1;
        }
        stdout.WriteLine($"gate text passed: no tool attribution in {Py.SplitLines(text).Count} lines, and the issue is named");
        return 0;
    }

    /// <summary>
    /// The text in file, or on standard input for `-`, as UTF-8 whatever the console's code page. A file is read as
    /// Python reads text, with \r\n and \r made \n; standard input as it comes.
    /// </summary>
    public static string ReadText(string file, Func<Stream> stdin)
    {
        if (file == "-")
        {
            using var input = stdin();
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        return Encoding.UTF8.GetString(File.ReadAllBytes(file)).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}

/// <summary>The git hooks behind the .githooks/ shims, which setup turns on.</summary>
internal static class Hooks
{
    // The line `git commit -v` writes above its diff, behind git's comment string; git drops it and all below it.
    // Everything above, comment lines included, is the message: with -m git keeps comment lines. Only that cut line
    // with a diff below it cuts the message. With -F or -m git keeps every line, a cut line included, so a trailer
    // below a line that merely looks like one would reach the commit. omniverse-mcp's hook cuts at any such line, with
    // any first word; this one is stricter on purpose.
    private const string CutLine = "------------------------ >8 ------------------------";

    // What core.commentChar=auto may pick: `#` when the message holds none, else the first of these that starts no line.
    public static readonly string[] AutoCommentChars = ["#", ";", "@", "!", "$", "%", "^", "&", "|", ":"];

    private static readonly PyRegex ZeroSha = new(@"^0+$");

    // What an agent reads as its rules when a session starts (REVIEW.md, The harness).
    private static readonly string[] AgentSetupPrefixes = [".claude/", ".agents/"];
    private static readonly string[] AgentSetupFiles = ["REVIEW.md", ".mcp.json"];
    private static readonly string[] AgentSetupNames = ["AGENTS.md", "CLAUDE.md"];

    private static void Say(TextWriter stderr, string hook, string text) => stderr.WriteLine($"{hook}: {text}");

    // What `git commit -v` writes below its cut line: git's diff headers and hunks, its comments, nothing else.
    private static readonly string[] DiffHeaders =
    [
        "diff ", "index ", "@@", "old mode ", "new mode ", "new file mode ", "deleted file mode ", "similarity index ",
        "dissimilarity index ", "rename from ", "rename to ", "copy from ", "copy to ", "Binary files ",
    ];

    /// <summary>
    /// The message git may record: everything above git's cut line, comment lines included, when a `diff --git` line
    /// follows it and every line below it is one git's diff or its comments could be; otherwise all of it. The lines
    /// are git's here, split at \n alone, so a char Python breaks lines at (\v, \x1c, U+2028) can't make a cut line,
    /// or hide a line below one. comments: the comment strings git may have written the cut line with.
    /// </summary>
    public static string MessageText(string raw, IReadOnlyCollection<string> comments)
    {
        var lines = raw.Split('\n').Select(line => line.EndsWith('\r') ? line[..^1] : line).ToList();
        var cut = lines.FindIndex(line => comments.Any(comment => line == $"{comment} {CutLine}"));
        var below = cut < 0 ? [] : lines.Skip(cut + 1).ToList();
        var isDiff = below.Any(line => line.StartsWith("diff --git ", StringComparison.Ordinal))
            && below.All(line => DiffShaped(line, comments));
        return isDiff ? Py.Strip(string.Join("\n", lines.Take(cut))) : Py.Strip(string.Join("\n", Py.SplitLines(raw)));
    }

    private static bool DiffShaped(string line, IReadOnlyCollection<string> comments) =>
        line.Length == 0
        || line[0] is ' ' or '+' or '-' or '\\'
        || DiffHeaders.Any(header => line.StartsWith(header, StringComparison.Ordinal))
        || comments.Any(comment => line.StartsWith(comment, StringComparison.Ordinal));

    /// <summary>
    /// The comment strings from `git config --get-regexp` of core.commentChar and core.commentString, which are
    /// aliases: the last one set wins, `#` when neither is, and git's candidates for `auto`.
    /// </summary>
    public static IReadOnlyCollection<string> CommentStrings(string configOutput)
    {
        var value = Py.SplitLines(configOutput).Where(line => line.Contains(' ', StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..]).LastOrDefault() ?? "";
        return value == "auto" ? AutoCommentChars : [value.Length > 0 ? value : "#"];
    }

    /// <summary>Refuse a message with tool attribution; 1 refuses the commit.</summary>
    public static int CommitMsg(string path, TextWriter stderr, IReadOnlyCollection<string>? comments = null)
    {
        comments ??= CommentStrings(Proc.Git(["config", "--get-regexp", @"^core\.comment(char|string)$"]).Stdout);
        // As git reads it, \r and all: where the message is cut is git's decision.
        var text = MessageText(Encoding.UTF8.GetString(File.ReadAllBytes(path)), comments);
        const string Hint = "take those lines out and commit again (AGENTS.md, Commits and PRs)";
        return Lanes.ReportAttribution(text, "commit-msg", Hint, null, stderr);
    }

    /// <summary>
    /// (range, local sha) for each ref a push updates, from the lines git gives pre-push on stdin:
    /// `&lt;local ref&gt; &lt;local sha&gt; &lt;remote ref&gt; &lt;remote sha&gt;`. A deletion sends nothing and is left out. A
    /// new branch, or a remote commit this clone never fetched, is compared with base.
    /// </summary>
    public static List<(string Range, string Local)> PushRanges(IEnumerable<string> lines, string baseRef, Func<string, bool> known)
    {
        var pushed = new List<(string, string)>();
        foreach (var line in lines)
        {
            var parts = Py.Split(line);
            if (parts.Count != 4 || ZeroSha.Match(parts[1]) is not null) continue;
            var (local, remote) = (parts[1], parts[3]);
            var since = ZeroSha.Match(remote) is null && known(remote) ? remote : baseRef;
            pushed.Add(($"{since}..{local}", local));
        }
        return pushed;
    }

    /// <summary>
    /// Why the working tree can't stand for what a push sends, or null when it can: every pushed commit is HEAD, and
    /// nothing is changed or untracked. pushed and head are commit ids.
    /// </summary>
    public static string? TreeSkipReason(IEnumerable<string> pushed, string head, bool dirty)
    {
        var other = pushed.Where(sha => sha != head).ToList();
        if (other.Count > 0) return $"the push sends {other[0][..Math.Min(10, other[0].Length)]}, not HEAD";
        return dirty ? "it has changes or untracked files that are not pushed" : null;
    }

    /// <summary>The gate's quick lane on what the push sends.</summary>
    public static int PrePush(List<string> lines, TextWriter stdout, TextWriter stderr)
    {
        var pushed = PushRanges(lines, Repo.Base, sha => Proc.Git(["cat-file", "-e", $"{sha}^{{commit}}"]).ExitCode == 0);
        if (pushed.Count == 0)
        {
            Say(stderr, "pre-push", "the push only deletes, so there is nothing to check");
            return 0;
        }
        var commits = pushed.Select(push => Py.Strip(Proc.Git(["rev-parse", $"{push.Local}^{{commit}}"]).Stdout)).ToList();
        var head = Py.Strip(Proc.Git(["rev-parse", "HEAD"]).Stdout);
        var dirty = Py.Strip(Proc.Git(["status", "--porcelain"]).Stdout).Length > 0;
        var scope = Scope.Of([.. pushed.Select(push => push.Range)], TreeSkipReason(commits, head, dirty));
        if (scope.TreeSkipped is not null)
            scope.TreeSkipped += "; run dotnet run eng/gate.cs -- quick on the pushed commit to check it";
        return Lanes.RunLane("quick", scope, stdout);
    }

    /// <summary>True for an agent's rules: AGENTS.md and CLAUDE.md anywhere, REVIEW.md, .claude/, .agents/, .mcp.json.</summary>
    public static bool IsAgentSetup(string path)
    {
        var shown = path.Replace('\\', '/');
        if (shown.StartsWith("./", StringComparison.Ordinal)) shown = shown[2..];
        return AgentSetupPrefixes.Any(prefix => shown.StartsWith(prefix, StringComparison.Ordinal))
            || AgentSetupFiles.Contains(shown)
            || AgentSetupNames.Contains(shown[(shown.LastIndexOf('/') + 1)..]);
    }

    /// <summary>Name the AI setup files that differ between revs: an agent read the old ones when its session started.</summary>
    private static int SetupChanged(string hook, string[] revs, TextWriter stderr)
    {
        var changed = Py.SplitLines(Proc.Git(["diff", "--name-only", .. revs]).Stdout).Where(IsAgentSetup).ToList();
        if (changed.Count > 0)
        {
            Say(stderr, hook, "the AI setup changed; an agent re-reads those it already read, or starts a new session:");
            foreach (var name in changed) Say(stderr, hook, $"  {name}");
        }
        return 0;
    }

    /// <summary>True while a rebase runs: its checkout goes from the branch's tip to the commit it rebases onto.</summary>
    private static bool Rebasing() =>
        new[] { "rebase-merge", "rebase-apply" }.Any(name =>
            Path.Exists(Path.Combine(Repo.Root, Py.Strip(Proc.Git(["rev-parse", "--git-path", name]).Stdout))));

    /// <summary>After a branch switch. A file checkout, the same commit and a new worktree (old is the null id) say nothing.</summary>
    public static int PostCheckout(string old, string @new, string branch, TextWriter stderr)
    {
        if (branch != "1" || old == @new || old.Trim('0').Length == 0) return 0;
        // A rebase compares only what the new base changed, not the branch's own commits it is about to replay.
        return SetupChanged("post-checkout", Rebasing() ? [$"{old}...{@new}"] : [old, @new], stderr);
    }

    /// <summary>After a merge or a pull: ORIG_HEAD is the commit before it.</summary>
    public static int PostMerge(TextWriter stderr)
    {
        var old = Py.Strip(Proc.Git(["rev-parse", "-q", "--verify", "ORIG_HEAD"]).Stdout);
        return old.Length > 0 ? SetupChanged("post-merge", [old, "HEAD"], stderr) : 0;
    }
}

/// <summary>Set up a clone once, and link skills for Codex. Safe to run again. Every setting is this repo's own.</summary>
internal static class Setup
{
    private static readonly (string Name, bool Needed, string Why)[] Tools =
    [
        ("dotnet", true, "runs the hooks and the gate, and builds the library: https://dotnet.microsoft.com/download"),
        ("gitleaks", false, "the gate's secret scan, skipped without it: https://github.com/gitleaks/gitleaks"),
        ("gh", false, "the skills' issues and PRs: https://cli.github.com, then gh auth login"),
    ];

    /// <summary>Set a value in this repo's git config; say whether it changed.</summary>
    public static string SetConfig(string root, string key, string value)
    {
        if (Py.Strip(Proc.Git(["config", "--local", "--get", key], root).Stdout) == value) return $"{key}={value} (already set)";
        var done = Proc.Git(["config", "--local", key, value], root);
        return done.ExitCode != 0
            ? $"TO DO: git config --local {key} {value} failed: {Py.Strip(done.Stderr)}"
            : $"{key}={value} (set)";
    }

    /// <summary>On Linux and macOS git skips a hook without the executable bit; a copy or an unzip may drop it.</summary>
    private static List<string> MakeHooksExecutable(string root)
    {
        var done = new List<string>();
        if (OperatingSystem.IsWindows() || !Directory.Exists(Path.Combine(root, Repo.HooksDir))) return done;
        foreach (var hook in Directory.GetFiles(Path.Combine(root, Repo.HooksDir)).Order(StringComparer.Ordinal))
        {
            var mode = File.GetUnixFileMode(hook);
            if ((mode & UnixFileMode.UserExecute) != 0) continue;
            File.SetUnixFileMode(hook, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            done.Add($"made {Repo.HooksDir}/{Path.GetFileName(hook)} executable");
        }
        return done;
    }

    /// <summary>True when this machine lets this user make a symlink; on Windows that takes Developer Mode.</summary>
    public static bool SymlinksWork()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gate-symlink-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "target"));
            Directory.CreateSymbolicLink(Path.Combine(dir, "link"), "target");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            SelfTest.DeleteTree(dir);
        }
    }

    /// <summary>
    /// Paths under .agents/skills that differ on disk from git's index, or that git doesn't track. A link only staged,
    /// as link leaves it, is no change: checking it out writes what the index holds.
    /// </summary>
    public static List<string> ChangedLinks(string root) =>
    [
        // Each line is "XY path": Y is the working tree's side, " " when the file on disk is what the index holds.
        .. Py.SplitLines(Proc.Git(["status", "--porcelain", "--", Repo.SkillLinksDir], root).Stdout)
            .Where(line => line.Length > 3 && line[1] != ' ')
            .Select(line => line[3..]),
    ];

    /// <summary>
    /// Where symlinks work, have git check the skill links out as real symlinks, which Codex follows. Elsewhere they
    /// stay plain files holding their target: git's index still has them as links, so every commit keeps them.
    /// </summary>
    public static List<string> CheckoutLinks(string root, Func<bool> canSymlink)
    {
        if (!canSymlink())
        {
            return
            [
                $"this machine can't make symlinks (Windows without Developer Mode), so {Repo.SkillLinksDir} holds plain "
                + "files and Codex finds no skills there; AGENTS.md names them, and each is a SKILL.md to read",
            ];
        }
        // A checkout overwrites what differs on disk from git's index, so a changed link is left for the developer to
        // look at. Checked before core.symlinks changes: with it false, a plain file holding the target is no change.
        var changed = ChangedLinks(root);
        if (changed.Count > 0)
        {
            return
            [
                $"warning: {Repo.SkillLinksDir} has local changes, so it was not checked out again: "
                + $"{string.Join(", ", changed)}; see git status -- {Repo.SkillLinksDir}, then run this again",
            ];
        }
        var lines = new List<string> { SetConfig(root, "core.symlinks", "true") };
        if (Py.Strip(Proc.Git(["ls-files", "--", Repo.SkillLinksDir], root).Stdout).Length == 0) return lines;
        var done = Proc.Git(["checkout", "--", Repo.SkillLinksDir], root);
        lines.Add(done.ExitCode != 0
            ? $"TO DO: git checkout -- {Repo.SkillLinksDir} failed: {Py.Strip(done.Stderr)}"
            : $"{Repo.SkillLinksDir} checked out as symlinks");
        return lines;
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Write `.agents/skills/&lt;name&gt;` into git's index as a symlink (mode 120000) to the skill, then check it out.
    /// git puts a symlink on disk where it can, and otherwise a plain file holding the target; either way the commit
    /// has the link. Git Bash's `ln -s` copies the folder instead, which is why this writes the index directly.
    /// </summary>
    public static (bool Ok, string Line) Link(string root, string name)
    {
        if (!File.Exists(Path.Combine(root, Repo.SkillsDir, name, "SKILL.md")))
            return (false, $"no skill {name}: {Repo.SkillsDir}/{name}/SKILL.md doesn't exist");
        var path = $"{Repo.SkillLinksDir}/{name}";
        var onDisk = Path.Combine(root, path);
        if (Directory.Exists(onDisk) && !IsSymlink(onDisk))
            return (false, $"{path} is a folder (a copy, as Git Bash's ln -s makes): delete it, then run this again");
        var target = $"../../{Repo.SkillsDir}/{name}";
        var blob = Proc.Git(["hash-object", "-w", "--stdin"], root, target);
        if (blob.ExitCode != 0) return (false, $"linking {path} failed: {Py.Strip(blob.Stderr)}");
        var sha = Py.Strip(blob.Stdout);
        var listed = Py.Split(Proc.Git(["ls-files", "-s", "--", path], root).Stdout);
        if (listed.Count >= 2 && listed[0] == Repo.LinkMode && listed[1] == sha && (IsSymlink(onDisk) || File.Exists(onDisk)))
            return (true, $"{path} -> {target} (already linked)");
        var added = Proc.Git(["update-index", "--add", "--cacheinfo", $"{Repo.LinkMode},{sha},{path}"], root);
        if (added.ExitCode != 0) return (false, $"linking {path} failed: {Py.Strip(added.Stderr)}");
        var done = Proc.Git(["checkout", "--", path], root);
        if (done.ExitCode != 0)
            return (false, $"{path} is staged as a link, but git couldn't check it out: {Py.Strip(done.Stderr)}");
        var kind = IsSymlink(onDisk) ? "a symlink" : "a plain file holding the target (git makes no symlinks here)";
        return (true, $"{path} -> {target} (linked in git's index; on disk {kind}). Commit it with the skill.");
    }

    /// <summary>Link each named skill, or every skill in .claude/skills; 1 when one failed.</summary>
    public static int LinkAll(string root, IReadOnlyList<string> names, TextWriter stdout)
    {
        var failed = 0;
        foreach (var name in names.Count > 0 ? names : [.. Lanes.SkillFolders(root).Select(skill => skill.Name)])
        {
            var (ok, line) = Link(root, name);
            stdout.WriteLine(line);
            if (!ok) failed++;
        }
        return failed > 0 ? 1 : 0;
    }

    /// <summary>Configure the repo at root and print what was done; 1 when something is left for you to do.</summary>
    public static int Run(string root, Func<string, string?> which, Func<bool> canSymlink, TextWriter stdout)
    {
        if (Proc.Git(["rev-parse", "--git-dir"], root).ExitCode != 0)
        {
            stdout.WriteLine($"not a git repository: {root}");
            return 1;
        }
        var config = new List<string>
        {
            $"{SetConfig(root, "core.hooksPath", Repo.HooksDir)}: the hooks in .githooks/: commit-msg, pre-push, post-checkout, post-merge",
            File.Exists(Path.Combine(root, Repo.BlameIgnoreRevs))
                ? $"{SetConfig(root, "blame.ignoreRevsFile", Repo.BlameIgnoreRevs)}: git blame skips the reformat commits listed there"
                : $"blame.ignoreRevsFile: not set, as there is no {Repo.BlameIgnoreRevs} yet",
        };
        var hooks = MakeHooksExecutable(root);
        var done = new List<string>();
        foreach (var (title, lines) in new[]
                 {
                     ("git config (this repo only)", config),
                     ("hooks", hooks.Count > 0 ? hooks : ["executable as stored"]),
                     ("skill links", CheckoutLinks(root, canSymlink)),
                 })
        {
            stdout.WriteLine($"{title}:");
            foreach (var line in lines) stdout.WriteLine($"  {line}");
            done.AddRange(lines);
        }
        var todo = done.Count(line => line.StartsWith("TO DO", StringComparison.Ordinal));
        stdout.WriteLine("tools:");
        foreach (var (name, needed, why) in Tools)
        {
            if (which(name) is not null) stdout.WriteLine($"  {name}: found");
            else if (needed)
            {
                todo++;
                stdout.WriteLine($"  TO DO: {name} is missing; the hooks skip themselves without it. Install: {why}");
            }
            else stdout.WriteLine($"  {name}: missing (optional: {why})");
        }
        stdout.WriteLine(todo == 0 ? "done." : $"done, with {todo} thing(s) to do above.");
        return todo > 0 ? 1 : 0;
    }
}

/// <summary>Reads the command line, as argparse would: a flag the lane doesn't read stops the command with exit code 2.</summary>
internal static class Cli
{
    private const string Usage =
        "usage: dotnet run eng/gate.cs -- quick|full [--base <ref>] [--head <ref>] | text --file <path>|- | selftest | setup | link [skill...]";

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<Stream> stdin)
    {
        if (args.Length == 0) return Error(stderr, "name a lane: quick, full, text, selftest, setup or link");
        var (command, rest) = (args[0], args[1..]);
        switch (command)
        {
            case "quick" or "full" or "text":
                return RunLane(command, rest, stdout, stderr, stdin);
            case "selftest":
                return rest.Length > 0 ? Error(stderr, "selftest takes no arguments") : SelfTest.Lane(stdout);
            case "setup":
                return rest.Length > 0
                    ? Error(stderr, "setup takes no arguments; link [skill...] links skills")
                    : Setup.Run(Repo.Root, Proc.Which, Setup.SymlinksWork, stdout);
            case "link":
                return Setup.LinkAll(Repo.Root, rest, stdout);
            case "commit-msg" when rest.Length > 0:
                return Hooks.CommitMsg(rest[0], stderr);
            case "pre-push":
                return Hooks.PrePush(Py.SplitLines(Lanes.ReadText("-", stdin)), stdout, stderr);
            case "post-checkout":
                return Hooks.PostCheckout(rest.ElementAtOrDefault(0) ?? "", rest.ElementAtOrDefault(1) ?? "",
                    rest.ElementAtOrDefault(2) ?? "0", stderr);
            case "post-merge":
                return Hooks.PostMerge(stderr);
            default:
                return Error(stderr, $"unknown lane or hook, or a missing argument: {string.Join(' ', args)}");
        }
    }

    private static int RunLane(string lane, string[] rest, TextWriter stdout, TextWriter stderr, Func<Stream> stdin)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rest.Length; i++)
        {
            var eq = rest[i].IndexOf('=', StringComparison.Ordinal);
            var flag = eq > 0 ? rest[i][..eq] : rest[i];
            if (flag is not ("--base" or "--head" or "--file")) return Error(stderr, $"unrecognized argument: {rest[i]}");
            var value = eq > 0 ? rest[i][(eq + 1)..] : i + 1 < rest.Length ? rest[++i] : null;
            if (value is null) return Error(stderr, $"{flag} needs a value");
            options[flag] = value;
        }
        // A flag the lane doesn't read is refused, so a mistyped command never passes having checked something else.
        if (lane == "text")
        {
            if (options.ContainsKey("--base") || options.ContainsKey("--head"))
                return Error(stderr, "text checks one text: --base and --head are for quick and full");
            if (!options.TryGetValue("--file", out var file))
                return Error(stderr, "text needs --file <path>, or --file - to read standard input");
            return Lanes.TextLane(Lanes.ReadText(file, stdin), stdout, stderr);
        }
        if (options.ContainsKey("--file")) return Error(stderr, $"{lane} checks the branch, not a file: --file is for text");
        var scope = Lanes.CliScope(options.GetValueOrDefault("--base", Repo.Base), options.GetValueOrDefault("--head", "HEAD"));
        return Lanes.RunLane(lane, scope, stdout);
    }

    private static int Error(TextWriter stderr, string message)
    {
        stderr.WriteLine(Usage);
        stderr.WriteLine($"gate: error: {message}");
        stderr.Flush();
        return 2;
    }
}

/// <summary>
/// The gate's own tests, run by quick and full and by the selftest lane. The attribution cases are omniverse-mcp's, word
/// for word; the cases under "Python's meaning" pin each place Python and .NET differ, with the result Python gives.
/// Ids and links are invented.
/// </summary>
internal static class SelfTest
{
    // Every form of tool attribution commit-msg refuses and the gate finds on a branch.
    private static readonly string[] Refused =
    [
        "Co-Authored-By: Claude <noreply@anthropic.com>",
        "Co-authored-by: Claude Opus 4.6 <noreply@anthropic.com>",
        "Co-authored-by: Claude Martin <noreply@anthropic.com>",
        "Co-authored-by: Claude Sonnet 4.5 (1M context) <claude@example.org>",
        "Co-authored-by: GitHub Copilot <copilot@example.org>",
        "co-authored-by: Copilot <175728472+Copilot@users.noreply.github.com>",
        "Co-authored-by: Cursor Agent <cursoragent@cursor.com>",
        "Co-authored-by: Codex <codex@openai.com>",
        "Co-authored-by: aider (gpt-4o) <aider@example.org>",
        "Co-authored-by: Gemini 2.5 Pro",
        "Co-authored-by: devin-ai-integration[bot] <158243242+devin-ai-integration[bot]@users.noreply.github.com>",
        "Co-authored-by: gemini-code-assist[bot] <176961590+gemini-code-assist[bot]@users.noreply.github.com>",
        "Co-authored-by: claude[bot] <1+claude[bot]@users.noreply.github.com>",
        "Co-authored-by: Copilot <2+copilot-swe-agent[bot]@users.noreply.github.com>",
        "Co-authored-by: copilot-swe-agent[bot] <198982749+Copilot@users.noreply.github.com>",
        "Co-authored-by: coderabbitai[bot] <136622811+coderabbitai[bot]@users.noreply.github.com>",
        "Co-authored-by: google-labs-jules[bot] <161369871+google-labs-jules[bot]@users.noreply.github.com>",
        "   Co-Authored-By: Claude <noreply@anthropic.com>",
        "# Co-Authored-By: Claude <noreply@anthropic.com>",
        "\U0001F916 Generated with [Claude Code](https://claude.com/claude-code)",
        "Generated with Claude Code",
        "# Generated with Claude Code",
        "- Generated by ChatGPT",
        "Written with GPT-4o",
        "Claude-Session: https://claude.ai/code/session_0000example",
        "https://claude.ai/code/session_0000example",
        "See https://chatgpt.com/codex/tasks/task_e_0000example",
    ];

    // Text a commit or a PR may hold: people as co-authors, among them people named as a tool is; the bots GitHub names a
    // co-author when a person pushes onto their PR; and sentences that name a tool without crediting it.
    private static readonly string[] Allowed =
    [
        "Co-authored-by: Ada Lovelace <ada@example.org>",
        "Co-authored-by: Claude Martin <claude.martin@example.fr>",
        "Co-authored-by: Devin Smith <devin@example.org>",
        "Co-authored-by: Gemini Ortiz <gemini.ortiz@example.com>",
        "Co-authored-by: Claudette Weber <claudette@example.org>",
        "Co-authored-by: dependabot[bot] <49699333+dependabot[bot]@users.noreply.github.com>",
        "Co-authored-by: github-actions[bot] <41898282+github-actions[bot]@users.noreply.github.com>",
        "Document how Codex finds the skills in .agents/skills",
        "Images generated by Kit's renderer come back black without a light",
        "Let Claude Desktop users frame a prim with inspect_prim",
        "Refs #2",
    ];

    private static readonly (string Who, bool Tool)[] CoAuthors =
    [
        ("Claude <claude@example.org>", true),
        ("Claude Opus 4.6", true),
        ("GitHub Copilot <copilot@example.org>", true),
        ("Jane Doe <noreply@anthropic.com>", true),
        ("claude[bot] <1+claude[bot]@users.noreply.github.com>", true),
        ("devin-ai-integration[bot] <3+devin-ai-integration[bot]@users.noreply.github.com>", true),
        ("someone[bot] <5+someone[bot]@users.noreply.github.com>", true),
        ("coderabbitai[bot] <136622811+coderabbitai[bot]@users.noreply.github.com>", true),
        ("Jules <161369871+google-labs-jules[bot]@users.noreply.github.com>", true),
        ("dependabot-ai[bot] <6+dependabot-ai[bot]@users.noreply.github.com>", true),
        ("Dependabot[bot] <49699333+Dependabot[bot]@users.noreply.github.com>", false),
        ("Claude <49699333+dependabot[bot]@users.noreply.github.com>", true),
        ("GitHub Copilot <41898282+github-actions[bot]@users.noreply.github.com>", true),
        ("claude[bot] <41898282+github-actions[bot]@users.noreply.github.com>", true),
        ("copilot-swe-agent[bot] <49699333+dependabot[bot]@users.noreply.github.com>", true),
        ("renovate[bot] <41898282+github-actions[bot]@users.noreply.github.com>", false),
        ("Claude Code Action <41898282+github-actions[bot]@users.noreply.github.com>", true),
        ("claude [bot] <41898282+github-actions[bot]@users.noreply.github.com>", true),
        ("Jane Doe <41898282+github-actions[bot]@users.noreply.github.com>", true),
        ("Ada <dependabot[bot]+claude[bot]@users.noreply.github.com>", true),
        ("dependabot[bot] <1+claude[bot]@users.noreply.github.com>", true),
        ("dependabot[bot] <49699333+dependabot[bot]@users.noreply.github.com>", false),
        ("github-actions[bot] <41898282+github-actions[bot]@users.noreply.github.com>", false),
        ("renovate[bot] <4+renovate[bot]@users.noreply.github.com>", false),
        ("Claude Martin <claude.martin@example.fr>", false),
        ("Devin Smith <devin@example.org>", false),
        ("Ada Lovelace <noreply@example.org>", false),
    ];

    // Python's meaning: each line where .NET's own regex, case folding, line breaks or strip() would decide otherwise,
    // with what Python's gate finds in it.
    private static readonly (string Line, string[] Found, string Why)[] PythonMeaning =
    [
        ("Generated with Cop\u0131lot", ["Generated with Cop\u0131lot"], "IGNORECASE matches i with dotless \u0131"),
        ("A\u017F\u017Fi\u017Fted by Claude", ["A\u017F\u017Fi\u017Fted by Claude"], "IGNORECASE matches s with long \u017F"),
        ("Generated with Gem\u0130ni", ["Generated with Gem\u0130ni"], "IGNORECASE matches i with \u0130"),
        ("Co-authored-by: Claude Opus thin\u212Aing <x@example.org>", ["Co-authored-by: Claude Opus thin\u212Aing <x@example.org>"],
            "IGNORECASE matches k with the Kelvin sign"),
        ("\U0001d400Co-authored-by: Claude <noreply@anthropic.com>", [], "a letter outside the BMP is \\w, so [^\\w]* stops at it"),
        ("\u0301Co-authored-by: Claude <noreply@anthropic.com>", ["\u0301Co-authored-by: Claude <noreply@anthropic.com>"],
            "a combining mark is no \\w"),
        ("\u00B2Generated with Claude Code", [], "a superscript two has a numeric value, so it is \\w"),
        ("\u203FGenerated with Claude Code", ["\u203FGenerated with Claude Code"], "connector punctuation other than _ is no \\w"),
        ("Generated with Claude\u0301", ["Generated with Claude\u0301"], "\\b falls between a letter and a combining mark"),
        ("Co-authored-by: Claude\u001F", ["Co-authored-by: Claude"], "\\x1f is \\s, and strip() takes it"),
        ("Fix it\u001CCo-Authored-By: Claude <noreply@anthropic.com>", ["Co-Authored-By: Claude <noreply@anthropic.com>"],
            "splitlines() breaks at \\x1c"),
        ("Fix it\u2028Co-Authored-By: Claude <noreply@anthropic.com>", ["Co-Authored-By: Claude <noreply@anthropic.com>"],
            "splitlines() breaks at U+2028"),
        ("Fix it\u001FCo-Authored-By: Claude <noreply@anthropic.com>", [], "splitlines() does not break at \\x1f"),
        ("Co-authored-by: github-actions[bot] <41898282+g\u0130thub-actions[bot]@users.noreply.github.com>",
            ["Co-authored-by: github-actions[bot] <41898282+g\u0130thub-actions[bot]@users.noreply.github.com>"],
            "lower() keeps \u0130 from equalling i"),
        ("Co-authored-by: dependabot[bot] <49699333+dependabot[bot]@u\u017Fers.noreply.github.com>", [],
            "the bot address matches with \u017F, and the bot is allowed"),
        ("Co-authored-by: Claude\u00A0<noreply@anthropic.com>", ["Co-authored-by: Claude\u00A0<noreply@anthropic.com>"],
            "a no-break space is \\s"),
        ("\uE000Generated with Claude Code", ["\uE000Generated with Claude Code"], "a private-use char is no \\w"),
        ("Generated with gpt-\U0001d7ce", ["Generated with gpt-\U0001d7ce"], "a digit outside the BMP is \\d"),
    ];

    private static readonly (string Text, bool Named)[] PythonIssueRefs =
    [
        ("F\u0131xes #3", true), ("Refs #2\u0301", true), ("Refs #2\u00B2", false), ("Refs #\U0001d7ce", true),
        ("x\u001CRefs #2", false), ("\u00B2Refs #2", false),
    ];

    private const string GoodSkill = "---\nname: start-work\ndescription: >-\n  Use when someone wants to work on something: finds the issue,\n  writes the plan.\n---\n\n# Start a change\n";

    // What git writes into the message file for `git commit` with an editor, on a branch whose name holds a tool's name.
    private const string GitTemplate = "Add the gate\n\nRefs #4\n\n"
        + "# Please enter the commit message for your changes. Lines starting\n"
        + "# with '#' will be ignored, and an empty message aborts the commit.\n#\n"
        + "# On branch claude-code-session\n# Your branch is up to date with 'origin/claude-code-session'.\n#\n"
        + "# Changes to be committed:\n#\tnew file:   .claude/skills/write-skill/SKILL.md\n#\tmodified:   eng/gate.cs\n#\n";

    private const string PrText = "Run CI on every pull request\n\n## What and why\n\nThe gate and the tests on every PR.\n\nRefs #2\n";
    private const string CoAuthorLine = "Co-Authored-By: Claude <noreply@anthropic.com>";
    private const string Robot = "\U0001F916 Generated with [Claude Code](https://claude.com/claude-code)";
    private static readonly string A = new('a', 40), B = new('b', 40), C = new('c', 40), Zero = new('0', 40);

    private static string Message(string line) => $"Fix the capture's lighting\n\nWhy it changed.\n\n{line}\nRefs #2\n";

    private sealed class Tally
    {
        public int Cases { get; private set; }
        public List<string> Failures { get; } = [];
        public List<string> Skipped { get; } = [];

        /// <summary>A case that can't run here, such as one that needs gitleaks: it is neither passed nor failed.</summary>
        public void Skip(string name, string why) => Skipped.Add($"{name}: {why}");

        public void True(string name, bool ok, string detail = "")
        {
            Cases++;
            if (!ok) Failures.Add(detail.Length > 0 ? $"{name}: {detail}" : name);
        }

        public void Equal<T>(string name, T expected, T actual) =>
            True(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected {Show(expected)}, got {Show(actual)}");

        public void Same(string name, IEnumerable<string> expected, IEnumerable<string> actual)
        {
            var (want, got) = (expected.ToList(), actual.ToList());
            True(name, want.SequenceEqual(got, StringComparer.Ordinal), $"expected [{string.Join(" | ", want.Select(Show))}], got [{string.Join(" | ", got.Select(Show))}]");
        }

        public void Contains(string name, string text, string part) =>
            True(name, text.Contains(part, StringComparison.Ordinal), $"{Show(part)} is not in {Show(text)}");

        private static string Show<T>(T value) => value switch
        {
            null => "null",
            string text => "\"" + string.Concat(text.Select(c => c is < ' ' or > '~' ? $"\\u{(int)c:x4}" : c.ToString())) + "\"",
            _ => value.ToString() ?? "",
        };
    }

    /// <summary>The selftest lane: every case, then each failure.</summary>
    public static int Lane(TextWriter stdout)
    {
        var (cases, failures, skipped) = Run();
        foreach (var skip in skipped) stdout.WriteLine($"SKIP {skip}");
        foreach (var failure in failures) stdout.WriteLine($"FAIL {failure}");
        var also = skipped.Count > 0 ? $", {skipped.Count} skipped" : "";
        stdout.WriteLine(failures.Count == 0 ? $"selftest passed: {cases} cases{also}" : $"selftest FAILED: {failures.Count} of {cases} cases{also}");
        return failures.Count == 0 ? 0 : 1;
    }

    public static (int Cases, List<string> Failures, List<string> Skipped) Run()
    {
        var t = new Tally();
        var scratch = Path.Combine(Path.GetTempPath(), $"tableau-api-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            Attribution(t);
            TextLaneCases(t, scratch);
            Flags(t);
            Files(t);
            CommitMsgCases(t, scratch);
            PushCases(t);
            GitCases(t, scratch);
        }
        catch (Exception ex)
        {
            t.True("the selftest ran to its end", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            DeleteTree(scratch);
        }
        return (t.Cases, t.Failures, t.Skipped);
    }

    private static void Attribution(Tally t)
    {
        foreach (var line in Refused) t.Same($"found: {line}", [Py.Strip(line)], Rules.AttributionLines(Message(line)));
        foreach (var line in Allowed) t.Same($"allowed: {line}", [], Rules.AttributionLines(Message(line)));
        foreach (var (who, tool) in CoAuthors) t.Equal($"tool co-author: {who}", tool, Rules.ToolCoAuthor(who));
        t.Same("commit problems name the commit and the line", ["aaaaaaaaaa: Generated with Claude Code"],
            Rules.CommitProblems([(A, Message("Generated with Claude Code")), (B, Message("Refs #2"))]));
        t.Equal("parse_log reads each commit, its people and its whole message",
            "1111|Ada <ada@example.org>|Bo <bo@example.org>|Subject one\n\nBody;2222|Ada <>|Ada <>|Subject two",
            string.Join(";", Rules.ParseLog("1111\0Ada <ada@example.org>\0Bo <bo@example.org>\0Subject one\n\nBody\n\u0001\n"
                + "2222\0Ada <>\0Ada <>\0Subject two\n\u0001\n").Select(c => $"{c.Sha}|{c.Author}|{c.Committer}|{c.Message}")));

        const string Person = "Ada Lovelace <ada@example.org>";
        foreach (var (author, committer, expected) in new (string, string, string[])[]
                 {
                     ("Claude <noreply@anthropic.com>", Person, ["aaaaaaaaaa: the author is a tool: Claude <noreply@anthropic.com>"]),
                     (Person, "Claude Code <claude@example.org>", ["aaaaaaaaaa: the committer is a tool: Claude Code <claude@example.org>"]),
                     ("dependabot[bot] <49699333+dependabot[bot]@users.noreply.github.com>", "GitHub <noreply@github.com>", []),
                     (Person, "GitHub <noreply@github.com>", []),
                     ("Claude Martin <claude.martin@example.fr>", Person, []),
                 })
            t.Same($"a commit's people: {author}, {committer}", expected, Rules.IdentityProblems([new Commit(A, author, committer, "Fix it")]));

        // An identity is a tool by its address; by its name only when the name is more than a first name could be.
        foreach (var (who, tool) in new[]
                 {
                     ("Claude <claude.dupont@example.fr>", false), ("Devin <devin.okafor@example.org>", false),
                     ("Gemini <gemini@example.org>", false), ("Claude Martin <claude.martin@example.fr>", false),
                     ("GitHub <noreply@github.com>", false),
                     ("dependabot[bot] <49699333+dependabot[bot]@users.noreply.github.com>", false),
                     ("Claude <noreply@anthropic.com>", true), ("Claude Code <claude@example.org>", true),
                     ("GitHub Copilot <copilot@example.org>", true), ("Claude Opus 4.6 <claude@example.org>", true),
                     ("Gemini 2.5 Pro <gemini@example.org>", true), ("gpt-4o <gpt@example.org>", true),
                     ("Copilot <175728472+Copilot@users.noreply.github.com>", true), ("claude[bot] <claude@example.org>", true),
                     ("Jane Doe <41898282+github-actions[bot]@users.noreply.github.com>", true),
                     ("Codex <codex@openai.com>", false), ("Codex <noreply@openai.com>", true),
                 })
            t.Equal($"tool identity: {who}", tool, Rules.ToolIdentity(who));

        const string Scanned = "5:02PM INF 3 commits scanned.\n5:02PM INF scanned ~1 bytes (1) in 1ms\n5:02PM INF no leaks found";
        const string Failed = "5:02PM ERR [git] fatal: could not fetch 1ff0c42 from promisor remote\n"
            + "5:02PM ERR error=\"stderr is not empty\"\n5:02PM INF 0 commits scanned.\n5:02PM INF no leaks found";
        t.Equal("gitleaks: a clean scan passes", null, Rules.GitleaksProblem(Scanned));
        t.Equal("gitleaks: an error git log gave it fails the scan",
            "gitleaks logged an error, so the scan can't be trusted: 5:02PM ERR [git] fatal: could not fetch 1ff0c42 from promisor remote",
            Rules.GitleaksProblem(Failed));
        t.Equal("gitleaks: a fatal error fails the scan",
            "gitleaks logged an error, so the scan can't be trusted: 5:02PM FTL could not create log cmd",
            Rules.GitleaksProblem("5:02PM FTL could not create log cmd"));
        t.Equal("gitleaks: 0 commits scanned without an error passes, as for a range that only deletes", null,
            Rules.GitleaksProblem("5:02PM INF 0 commits scanned.\n5:02PM INF no leaks found"));

        foreach (var (line, found, why) in PythonMeaning) t.Same($"Python's meaning, {why}: {line}", found, Rules.AttributionLines(line));
        t.Equal("Python's meaning: \\x1f after a tool's name is stripped", true, Rules.ToolCoAuthor("Claude\u001F"));
        foreach (var (text, named) in PythonIssueRefs) t.Equal($"Python's meaning, issue named: {text}", named, Rules.IssueRefProblems(text).Count == 0);
        t.Same("Python's meaning: splitlines", ["a", "b\u001Fc", "d"], Py.SplitLines("a\u001Cb\u001Fc\u2028d"));
        t.Equal("Python's meaning: strip", "Claude", Py.Strip(" \u001FClaude\u001C\u00A0"));
        t.Equal("Python's meaning: the Kelvin sign lowers to k", "k", Py.Lower("\u212A"));
        t.True("Python's meaning: \u0130 does not lower to i", Py.Lower("\u0130") != "i");
        t.True("an escape Py.Translate does not know stops it", Throws(() => Py.Translate(@"\W")));
    }

    private static void TextLaneCases(Tally t, string scratch)
    {
        int Lane(string text, out string stdout, out string stderr)
        {
            var file = Path.Combine(scratch, "pr.txt");
            File.WriteAllText(file, text, new UTF8Encoding(false));
            var (output, error) = (new StringWriter(), new StringWriter());
            var code = Cli.Run(["text", "--file", file], output, error, () => Stream.Null);
            (stdout, stderr) = (output.ToString(), error.ToString());
            return code;
        }

        foreach (var reference in new[] { "Refs #2", "Closes #13", "Fixes #7", "closes #13", "- Refs #2" })
        {
            t.Equal($"text passes a PR that names its issue: {reference}", 0, Lane(PrText.Replace("Refs #2", reference), out var stdout, out _));
            t.True($"text says it passed: {reference}", stdout.StartsWith("gate text passed", StringComparison.Ordinal), stdout);
        }
        foreach (var line in new[] { CoAuthorLine, Robot })
        {
            t.Equal($"text refuses attribution: {line}", 1, Lane($"{PrText}\n{line}\n", out _, out var stderr));
            t.True($"text names the line: {line}", Py.SplitLines(stderr).Contains($"gate text: refused: tool attribution: {line}"), stderr);
        }
        foreach (var (ending, expected) in new[]
                 {
                     ("Closes #", "a `Refs #` or `Closes #` line has no issue number"),
                     ("Refs #  ", "a `Refs #` or `Closes #` line has no issue number"),
                     ("", "no line names the issue"),
                     ("See issue 2", "no line names the issue"),
                     ("<!-- Closes #<n> when this finishes the issue -->", "no line names the issue"),
                 })
        {
            t.Equal($"text refuses a PR that names no issue: {ending}", 1, Lane(PrText.Replace("Refs #2", ending), out _, out var stderr));
            t.Contains($"text says why: {ending}", stderr, $"gate text: refused: {expected}");
            t.True($"text finds no attribution: {ending}", !stderr.Contains("tool attribution", StringComparison.Ordinal), stderr);
        }
        var (stdoutOf, stderrOf) = (new StringWriter(), new StringWriter());
        var bytes = Encoding.UTF8.GetBytes($"{PrText}\n{Robot}\n");
        t.Equal("text reads standard input as UTF-8", 1,
            Cli.Run(["text", "--file", "-"], stdoutOf, stderrOf, () => new MemoryStream(bytes)));
        t.True("text names the line read from standard input",
            Py.SplitLines(stderrOf.ToString()).Contains($"gate text: refused: tool attribution: {Robot}"), stderrOf.ToString());
    }

    private static void Flags(Tally t)
    {
        foreach (var (argv, expected) in new (string[], string)[]
                 {
                     (["text"], "text needs --file"),
                     (["text", "--file", "-", "--base", "origin/main"], "--base and --head are for quick and full"),
                     (["text", "--file", "-", "--head", "HEAD"], "--base and --head are for quick and full"),
                     (["quick", "--file", "pr.txt"], "--file is for text"),
                     (["full", "--file", "-"], "--file is for text"),
                 })
        {
            var stderr = new StringWriter();
            t.Equal($"a flag the lane does not read is refused: {string.Join(' ', argv)}", 2,
                Cli.Run(argv, new StringWriter(), stderr, () => Stream.Null));
            t.Contains($"it says which: {string.Join(' ', argv)}", stderr.ToString(), expected);
        }
    }

    private static void Files(Tally t)
    {
        t.Same("AGENTS.md may have 150 lines", [], Rules.AgentsMdProblems(string.Concat(Enumerable.Repeat("line\n", 150))));
        t.Same("AGENTS.md over 150 lines fails", ["AGENTS.md has 151 lines; keep it within 150"],
            Rules.AgentsMdProblems(string.Concat(Enumerable.Repeat("line\n", 151))));

        var fields = Rules.Frontmatter(GoodSkill);
        t.Equal("frontmatter joins a block and marks it not plain",
            "name=start-work/True;description=Use when someone wants to work on something: finds the issue, writes the plan./False",
            string.Join(";", fields.Select(field => $"{field.Key}={field.Value.Value}/{field.Value.Plain}")));
        t.Equal("frontmatter reads quoted values", "name=x/False",
            string.Join(";", Rules.Frontmatter("---\nname: \"x\"\n---\n").Select(field => $"{field.Key}={field.Value.Value}/{field.Value.Plain}")));
        foreach (var text in new[] { "# no frontmatter\n", "---\nname: x\n", "---\nname: x\nname: y\n---\n", "---\njust words\n---\n" })
            t.True($"frontmatter refuses what it cannot read: {text}", Throws(() => Rules.Frontmatter(text)));

        t.Same("a good skill has no problems", [], Rules.SkillProblems("start-work", GoodSkill));
        var bom = Path.Combine(Path.GetTempPath(), $"gate-bom-{Guid.NewGuid():N}.md");
        File.WriteAllText(bom, "\uFEFF" + GoodSkill, new UTF8Encoding(false));
        t.Same("Python's meaning: a skill read with a byte order mark does not start with ---",
            [".claude/skills/start-work/SKILL.md: it does not start with a --- line"],
            Rules.SkillProblems("start-work", Lanes.ReadText(bom, () => Stream.Null)));
        File.Delete(bom);
        static string Plain(string description) => $"---\nname: start-work\ndescription: {description}\n---\n";
        foreach (var (folder, text, expected) in new[]
                 {
                     ("finish-work", GoodSkill, "it must equal the folder name"),
                     ("start-work", "---\nname: start-work\n---\n", "description is missing"),
                     ("start-work", GoodSkill.Replace("name: start-work", "name: start-work\nallowed-tools: Read"), "allowed-tools"),
                     ("start-work", GoodSkill.Replace("Use when someone", "Helps when someone"), "\"Use when ...\""),
                     ("start-work", GoodSkill.Replace("writes the plan.", new string('x', 1024)), "characters; keep it within 1024"),
                     ("start-work", Plain("Use when: anything"), "write it as a >- block"),
                     ("start-work", Plain("Use when ready:"), "write it as a >- block"),
                     ("start-work", Plain("Use when:\tanything"), "write it as a >- block"),
                     ("start-work", Plain("Use when asked #2"), "write it as a >- block"),
                     ("Start_Work", GoodSkill.Replace("start-work", "Start_Work"), "lowercase letters, digits and hyphens"),
                     ("start-work", "no frontmatter\n", "it does not start with a --- line"),
                 })
        {
            var problems = Rules.SkillProblems(folder, text);
            t.True($"skill problems: {expected}", problems.Count == 1 && problems[0].Contains(expected, StringComparison.Ordinal),
                string.Join(" | ", problems));
        }
        t.Same("a plain description without a colon or a hash passes", [],
            Rules.SkillProblems("start-work", Plain("Use when someone starts work, as in issue 12")));

        var link = ("120000", ".agents/skills/start-work", "../../.claude/skills/start-work");
        var skills = new HashSet<string> { "start-work" };
        t.Same("a link to each skill passes", [], Rules.LinkProblems([link], skills));
        foreach (var (entries, expected) in new (List<(string, string, string)>, string)[]
                 {
                     ([("100644", link.Item2, link.Item3)], "is a plain file in git (mode 100644), not a link"),
                     ([(link.Item1, link.Item2, "../../.claude/skills/finish-work")], "points to"),
                     ([], ".agents/skills/start-work is missing"),
                     ([link, ("120000", ".agents/skills/gone", "../../.claude/skills/gone")], "leads to no skill"),
                     ([("100644", ".agents/skills/start-work/SKILL.md", "")], "holds files; it must be one link"),
                 })
        {
            var problems = Rules.LinkProblems(entries, skills);
            t.True($"link problems: {expected}", problems.Count == 1 && problems[0].Contains(expected, StringComparison.Ordinal),
                string.Join(" | ", problems));
        }

        t.Same("hooks stored as executable pass", [], Rules.HookProblems([("100755", ".githooks/commit-msg")]));
        t.Same("a hook added from Windows without the bit fails",
            [".githooks/pre-push is stored as mode 100644, so Linux and macOS skip it: git update-index --chmod=+x .githooks/pre-push"],
            Rules.HookProblems([("100755", ".githooks/commit-msg"), ("100644", ".githooks/pre-push")]));

        var root = Path.GetTempPath();
        List<(string, string)> stored = [("100755", ".githooks/commit-msg"), ("100755", ".githooks/pre-push")];
        foreach (var (hooksPath, status) in new[]
                 {
                     ("", "warn"), (".githooks", "pass"), ("./.githooks", "pass"), (".githooks/", "pass"),
                     (Path.Combine(root, ".githooks"), "pass"), (".git/hooks", "warn"), (Path.Combine(root, "elsewhere"), "warn"),
                 })
            t.Equal($"hooks with core.hooksPath {Show(hooksPath)}", status, Lanes.HooksResult(stored, hooksPath, root).Status);
        t.Contains("the hooks warning says how to turn them on", Lanes.HooksResult(stored, "", root).Detail, "dotnet run eng/gate.cs -- setup");
        t.Equal("hooks stored without the bit fail, whatever core.hooksPath says", "FAIL",
            Lanes.HooksResult([("100644", ".githooks/pre-push")], ".githooks", root).Status);
        t.Equal("no hooks in git's index fails", "FAIL", Lanes.HooksResult([], ".githooks", root).Status);
    }

    private static string Show(string text) => text.Length == 0 ? "unset" : text;

    private static void CommitMsgCases(Tally t, string scratch)
    {
        int CommitMsg(string text, out string stderr, string comment = "#")
        {
            var file = Path.Combine(scratch, "COMMIT_EDITMSG");
            File.WriteAllText(file, text, new UTF8Encoding(false));
            var error = new StringWriter();
            var code = Hooks.CommitMsg(file, error, comment == "auto" ? Hooks.AutoCommentChars : [comment]);
            stderr = error.ToString();
            return code;
        }

        foreach (var line in Refused)
        {
            t.Equal($"commit-msg refuses: {line}", 1, CommitMsg(Message(line), out var stderr));
            t.Same($"commit-msg says why: {line}",
                [$"commit-msg: refused: tool attribution: {Py.Strip(line)}", "commit-msg: take those lines out and commit again (AGENTS.md, Commits and PRs)"],
                Py.SplitLines(stderr));
        }
        foreach (var line in Allowed) t.Equal($"commit-msg accepts: {line}", 0, CommitMsg(Message(line), out _));
        foreach (var comment in new[] { "#", ";" })
        {
            var template = GitTemplate.Replace("\n#", $"\n{comment}");
            t.Equal($"commit-msg accepts the template git writes, comments {comment}", 0, CommitMsg(template, out _, comment));
            // With -m git keeps comment lines in the message; the diff `git commit -v` adds below the scissors never
            // reaches the commit, and a diff of the Refused list must not refuse the commit that changes it.
            var below = Below(comment);
            t.Equal($"commit-msg reads nothing below the scissors, comments {comment}", 0, CommitMsg(template + below, out _, comment));
            t.Equal($"commit-msg reads comment lines, comments {comment}", 1,
                CommitMsg($"{template}{comment} Generated with Claude Code\n{below}", out _, comment));
            t.Equal($"the message is what is above the scissors, comments {comment}", Py.Strip(template),
                Hooks.MessageText(template + below, [comment]));
        }

        // Only git's own cut line, with git's comment string and a diff below it, cuts the message: with -F or -m git
        // keeps every line, so anything else that looks like one would hide a trailer that reaches the commit.
        const string Probe = "Add trailer probe\n\nRefs #5\n\n; ------------------------ >8 ------------------------\n\n"
            + "Co-Authored-By: Claude <noreply@anthropic.com>\n";
        const string NoDiff = "Add probe\n\nWhy.\n\n# ------------------------ >8 ------------------------\n"
            + "Co-Authored-By: Claude <noreply@anthropic.com>\n";
        t.Equal("commit-msg reads past a cut line of another comment character", 1, CommitMsg(Probe, out var probeErr));
        t.True("it names the trailer below it",
            Py.SplitLines(probeErr).Contains("commit-msg: refused: tool attribution: Co-Authored-By: Claude <noreply@anthropic.com>"), probeErr);
        t.Equal("commit-msg reads past a cut line with no diff below it", 1, CommitMsg(NoDiff, out _));
        t.Equal("commit-msg cuts at the cut line of core.commentChar ;", 0,
            CommitMsg(GitTemplate.Replace("\n#", "\n;") + Below(";"), out _, ";"));
        t.Equal("a cut line of # with comments ; cuts nothing", 1, CommitMsg(GitTemplate + Below("#"), out _, ";"));
        t.Equal("core.commentChar auto: the cut line may use any of git's candidates", 0,
            CommitMsg(GitTemplate.Replace("\n#", "\n@") + Below("@"), out _, "auto"));
        t.Equal("a cut line of // for core.commentString //", 0, CommitMsg(GitTemplate.Replace("\n#", "\n//") + Below("//"), out _, "//"));
        t.Equal("the reviewer's message is read whole", Py.Strip(Probe), Hooks.MessageText(Probe, ["#"]));

        // Below git's cut line only git's diff and comments can stand: a line of anything else means git didn't write
        // what follows, and with -F it reaches the commit. Lines are git's, split at \n alone.
        foreach (var (name, message) in new[]
                 {
                     ("a trailer below a diff header", "Add scissors probe\n\nRefs #1\n\n# ------------------------ >8 ------------------------\n"
                         + "diff --git a/x b/x\n\nCo-Authored-By: Claude <noreply@anthropic.com>\n"),
                     ("a no-break space after the cut line", "Add nbsp probe\n\nRefs #1\n\n# ------------------------ >8 ------------------------\u00A0\n"
                         + "diff --git a/x b/x\n\nCo-Authored-By: Claude <noreply@anthropic.com>\n"),
                     ("a vertical tab, which Python breaks lines at", "Add vt probe\n\nRefs #1\n\n# ------------------------ >8 ------------------------\u000B"
                         + "diff --git a/x b/x\n\nCo-Authored-By: Claude <noreply@anthropic.com>\n"),
                     ("a trailer after git's own diff", GitTemplate + Below("#") + "Co-Authored-By: Claude <noreply@anthropic.com>\n"),
                 })
            t.Equal($"commit-msg reads past the cut line: {name}", 1, CommitMsg(message, out _));
        t.Equal("commit-msg cuts a message git wrote with \\r\\n", 0,
            CommitMsg((GitTemplate + Below("#")).Replace("\n", "\r\n"), out _));
        var headers = "# ------------------------ >8 ------------------------\n# Do not modify or remove the line above.\n"
            + "diff --git a/old.txt b/new.txt\nsimilarity index 90%\nrename from old.txt\nrename to new.txt\nold mode 100644\n"
            + "new mode 100755\nindex 1a2b3c4..5d6e7f8\n--- a/old.txt\n+++ b/new.txt\n@@ -1 +1 @@\n-Generated with Claude Code\n"
            + "+Co-Authored-By: Claude <noreply@anthropic.com>\n context\n\\ No newline at end of file\n"
            + "diff --git a/gone.bin b/gone.bin\ndeleted file mode 100644\nBinary files a/gone.bin and /dev/null differ\n"
            + "diff --git a/c.txt b/d.txt\ncopy from c.txt\ncopy to d.txt\ndiff --git a/n.txt b/n.txt\nnew file mode 100644\n";
        t.Equal("commit-msg cuts above every header git's diff writes", 0, CommitMsg("Add the gate\n\nRefs #4\n\n" + headers, out _));
        foreach (var (config, expected) in new[]
                 {
                     ("", "#"), ("core.commentchar ;\n", ";"), ("core.commentchar ;\ncore.commentstring //\n", "//"),
                     ("core.commentstring //\ncore.commentchar ;\n", ";"), ("core.commentchar auto\n", string.Concat(Hooks.AutoCommentChars)),
                 })
            t.Equal($"comment strings from git config: {Show(config.Replace("\n", " ").Trim())}", expected, string.Concat(Hooks.CommentStrings(config)));
    }

    private static string Below(string comment) =>
        $"{comment} ------------------------ >8 ------------------------\n"
        + $"{comment} Do not modify or remove the line above.\n{comment} Everything below it will be ignored.\n"
        + "diff --git a/eng/gate.cs b/eng/gate.cs\n" + string.Join("\n", Refused.Select(line => $"+        \"{line}\",")) + "\n";

    private static void PushCases(Tally t)
    {
        var lines = new[]
        {
            $"refs/heads/update {A} refs/heads/update {B}",
            $"refs/heads/new {C} refs/heads/new {Zero}",
            $"(delete) {Zero} refs/heads/gone {B}",
            $"refs/heads/forced {A} refs/heads/forced {C}",
            "",
        };
        t.Same("push ranges: what each ref sends", [$"{B}..{A}|{A}", $"origin/main..{C}|{C}", $"origin/main..{A}|{A}"],
            Hooks.PushRanges(lines, "origin/main", sha => sha == B).Select(push => $"{push.Range}|{push.Local}"));
        foreach (var (pushed, dirty, expected) in new (string[], bool, string?)[]
                 {
                     ([A], false, null),
                     ([A, A], false, null),
                     ([A], true, "it has changes or untracked files that are not pushed"),
                     ([A, B], false, "the push sends bbbbbbbbbb, not HEAD"),
                 })
            t.Equal($"the working tree stands for a push only of a clean HEAD: {pushed.Length} {dirty}", expected, Hooks.TreeSkipReason(pushed, A, dirty));

        foreach (var (path, expected) in new[]
                 {
                     ("AGENTS.md", true), ("Tableau.Api.Tests/CLAUDE.md", true), ("REVIEW.md", true), (".mcp.json", true),
                     (".claude/skills/start-work/SKILL.md", true), (".agents/skills/start-work", true),
                     ("Tableau.Api/TableauApiClient.cs", false), ("docs/.mcp.json.example", false), ("docs/plans/README.md", false),
                 })
            t.Equal($"agent setup: {path}", expected, Hooks.IsAgentSetup(path));
        foreach (var (old, @new, branch, why) in new[]
                 {
                     (A, B, "0", "file checkout"), (A, A, "1", "same commit"), (Zero, B, "1", "new worktree"),
                 })
        {
            var stderr = new StringWriter();
            t.Equal($"post-checkout says nothing for a {why}", 0, Hooks.PostCheckout(old, @new, branch, stderr));
            t.Equal($"post-checkout prints nothing for a {why}", "", stderr.ToString());
        }
    }

    private static void GitCases(Tally t, string scratch)
    {
        // A repo with an invented author and no hooks.
        Func<string[], string> NewRepo(string path)
        {
            var noHooks = Path.Combine(path, ".no-hooks");
            Directory.CreateDirectory(noHooks);
            string Git(params string[] args)
            {
                var done = Proc.Git(["-c", "user.name=Test", "-c", "user.email=test@example.org", "-c", "commit.gpgsign=false",
                    "-c", $"core.hooksPath={noHooks}", .. args], path);
                if (done.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)}: {Py.Strip(done.Stderr)}");
                return Py.Strip(done.Stdout);
            }
            Git("init", "-q", "-b", "main");
            return Git;
        }
        void CommitFile(Func<string[], string> git, string path, string name, string subject)
        {
            File.WriteAllText(Path.Combine(path, name), $"{name}\n");
            git(["add", "-A"]);
            git(["commit", "-q", "-m", subject]);
        }
        string Fresh(string name)
        {
            var path = Path.Combine(scratch, name);
            Directory.CreateDirectory(path);
            return path;
        }
        void AddSkill(string path)
        {
            var skill = Path.Combine(path, ".claude", "skills", "start-work");
            Directory.CreateDirectory(skill);
            File.WriteAllText(Path.Combine(skill, "SKILL.md"), GoodSkill, new UTF8Encoding(false));
        }

        var merges = Fresh("merges");
        var git = NewRepo(merges);
        CommitFile(git, merges, "a.txt", "Base");
        git(["switch", "-q", "-c", "feature"]);
        CommitFile(git, merges, "b.txt", "Feature work");
        git(["switch", "-q", "main"]);
        CommitFile(git, merges, "c.txt", "Main moves");
        git(["switch", "-q", "feature"]);
        git(["merge", "-q", "--no-ff", "main", "-m", "Merge main\n\nGenerated with Claude Code"]);
        var (commits, problem) = Lanes.ListCommits(["main..feature", "main..feature"], merges);
        t.Equal("list commits: no problem", null, problem);
        t.Same("list commits includes merges and lists each commit once", ["Merge main", "Feature work"],
            commits.Select(commit => Py.SplitLines(commit.Message)[0]));
        t.Same("a merge's message is checked", commits.Count > 0 ? [$"{commits[0].Sha[..10]}: Generated with Claude Code"] : ["(no commits)"],
            Rules.CommitProblems(commits.Select(commit => (commit.Sha, commit.Message))));
        t.Same("list commits reads each commit's author and committer", ["Test <test@example.org>|Test <test@example.org>"],
            commits.Select(commit => $"{commit.Author}|{commit.Committer}").Distinct());

        // A commit a tool made under its own name, beside one from a person and one from Dependabot.
        git(["switch", "-q", "main"]);
        git(["switch", "-q", "-c", "people"]);
        File.WriteAllText(Path.Combine(merges, "d.txt"), "d\n");
        git(["add", "-A"]);
        git(["commit", "-q", "-m", "Made by a tool", "--author", "Claude <noreply@anthropic.com>"]);
        File.WriteAllText(Path.Combine(merges, "e.txt"), "e\n");
        git(["add", "-A"]);
        git(["commit", "-q", "-m", "Bump a package", "--author", "dependabot[bot] <49699333+dependabot[bot]@users.noreply.github.com>"]);
        CommitFile(git, merges, "f.txt", "Made by a person");
        var (people, _) = Lanes.ListCommits(["main..people"], merges);
        var claude = people.FirstOrDefault(commit => commit.Message == "Made by a tool")?.Sha ?? "";
        t.Same("a commit a tool authored is refused, Dependabot's and a person's are not",
            [$"{claude[..Math.Min(10, claude.Length)]}: the author is a tool: Claude <noreply@anthropic.com>"],
            Rules.IdentityProblems(people));

        // gitleaks gets full commit ids: it splits its --log-opts at spaces.
        var fullRange = new Regex("^[0-9a-f]{40}\\.\\.[0-9a-f]{40}$");
        var (resolved, unresolved) = Lanes.ResolveRange("main..people", merges);
        t.True("a range resolves to two full commit ids", resolved is not null && fullRange.IsMatch(resolved), resolved ?? unresolved ?? "");
        (resolved, unresolved) = Lanes.ResolveRange(":/Main moves..feature", merges);
        t.True("a base found by its message, with a space in it, resolves", resolved is not null && fullRange.IsMatch(resolved),
            resolved ?? unresolved ?? "");
        var (_, bad) = Lanes.ResolveRange("nothing-here..people", merges);
        t.Equal("a range that doesn't resolve says which end", "can't resolve nothing-here in nothing-here..people to a commit", bad);

        GitleaksCases(t, scratch, NewRepo, CommitFile, Fresh);

        var noBase = Fresh("no-base");
        CommitFile(NewRepo(noBase), noBase, "a.txt", "Base");
        var (none, missing) = Lanes.ListCommits(["origin/main..HEAD"], noBase);
        t.True("list commits says how to fetch origin/main when it is missing", none.Count == 0 && missing is not null
            && missing.EndsWith(": git fetch origin main:refs/remotes/origin/main, then run it again", StringComparison.Ordinal),
            missing ?? "no problem");

        var linked = Fresh("link");
        git = NewRepo(linked);
        AddSkill(linked);
        var (ok, line) = Setup.Link(linked, "start-work");
        t.True("link writes the skill's link", ok, line);
        var listed = Py.Split(git(["ls-files", "-s", "--", ".agents/skills"]));
        t.True("the link is a symlink in git's index", listed.Count == 4 && listed[0] == "120000" && listed[3] == ".agents/skills/start-work",
            string.Join(' ', listed));
        if (listed.Count == 4)
        {
            var target = git(["cat-file", "blob", listed[1]]);
            t.Same("the gate accepts the link", [], Rules.LinkProblems([(listed[0], listed[3], target)], new HashSet<string> { "start-work" }));
        }
        var onDisk = Path.Combine(linked, ".agents", "skills", "start-work");
        t.True("the link is on disk", new FileInfo(onDisk).LinkTarget is not null || new DirectoryInfo(onDisk).LinkTarget is not null
            || (File.Exists(onDisk) && File.ReadAllText(onDisk) == "../../.claude/skills/start-work"));
        (ok, line) = Setup.Link(linked, "start-work");
        t.True("link again says it is linked", ok && line.EndsWith("(already linked)", StringComparison.Ordinal), line);

        var nothing = Fresh("nothing");
        NewRepo(nothing);
        (ok, line) = Setup.Link(nothing, "nothing");
        t.True("link refuses a skill that does not exist", !ok && line.Contains("doesn't exist", StringComparison.Ordinal), line);

        var edited = Fresh("edited");
        git = NewRepo(edited);
        git(["config", "core.symlinks", "false"]);
        AddSkill(edited);
        t.True("link in a clone without symlinks", Setup.Link(edited, "start-work").Ok);
        File.WriteAllText(Path.Combine(edited, ".agents", "skills", "start-work"), "edited by hand");
        var lines = Setup.CheckoutLinks(edited, () => true);
        t.True("setup leaves a changed skill link alone", lines.Count > 0
            && lines[0].StartsWith("warning: .agents/skills has local changes", StringComparison.Ordinal), string.Join(" | ", lines));
        t.Equal("the changed link keeps its text", "edited by hand", File.ReadAllText(Path.Combine(edited, ".agents", "skills", "start-work")));
        t.Equal("core.symlinks stays as it was", "false", git(["config", "core.symlinks"]));

        var staged = Fresh("staged");
        git = NewRepo(staged);
        git(["config", "core.symlinks", "false"]);
        AddSkill(staged);
        t.True("link a skill to stage", Setup.Link(staged, "start-work").Ok);
        t.Same("a link only staged is no change", [], Setup.ChangedLinks(staged));
        t.Equal("setup checks out a link that is only staged", "core.symlinks=true (set)",
            Setup.CheckoutLinks(staged, () => true).FirstOrDefault());
    }

    // gitleaks on commits that change no file's text, which it counts as no commit scanned and must pass, and on a
    // blobless clone whose remote is gone, where git log fails under it and the gate must fail. Skipped without gitleaks.
    private static void GitleaksCases(Tally t, string scratch, Func<string, Func<string[], string>> newRepo,
        Action<Func<string[], string>, string, string, string> commitFile, Func<string, string> fresh)
    {
        var exe = Proc.Which("gitleaks");
        var config = Path.Combine(Repo.Root, Repo.GitleaksConfig);
        string[] names = ["a commit that only deletes", "a commit that only renames", "a commit that only changes a mode",
            "a commit that adds an empty file", "a blobless clone whose remote is gone"];
        if (exe is null || !File.Exists(config))
        {
            foreach (var name in names) t.Skip($"gitleaks on {name}", exe is null ? "gitleaks is not installed" : $"no {Repo.GitleaksConfig}");
            return;
        }
        var repo = fresh("leaks");
        var git = newRepo(repo);
        foreach (var file in new[] { "a.txt", "b.txt", "c.txt" }) commitFile(git, repo, file, $"Add {file}");
        var steps = new (string Name, string[][] Commands)[]
        {
            (names[0], [["rm", "-q", "a.txt"]]),
            (names[1], [["mv", "b.txt", "b2.txt"]]),
            (names[2], [["update-index", "--chmod=+x", "c.txt"]]),
            (names[3], [["add", "-A"]]),
        };
        foreach (var (name, commands) in steps)
        {
            if (name == names[3]) File.WriteAllText(Path.Combine(repo, "empty.txt"), "");
            foreach (var command in commands) git(command);
            git(["commit", "-q", "-m", $"Change: {name}"]);
            var failed = Lanes.Gitleaks(exe, repo, config, "HEAD~1..HEAD");
            t.Equal($"gitleaks on {name} passes", "pass", failed is null ? "pass" : $"{failed.Detail}: {failed.Output}");
        }

        git(["config", "uploadpack.allowFilter", "true"]);
        var blobless = Path.Combine(scratch, "blobless");
        var cloned = Proc.Git(["clone", "-q", "--filter=blob:none", "--no-checkout", new Uri(repo).AbsoluteUri, blobless], scratch);
        var gone = new Uri(Path.Combine(scratch, "gone.git")).AbsoluteUri;
        if (cloned.ExitCode != 0 || Proc.Git(["remote", "set-url", "origin", gone], blobless).ExitCode != 0)
        {
            t.True($"gitleaks on {names[4]} fails", false, $"the clone failed: {Py.Strip(cloned.Stderr)}");
            return;
        }
        var result = Lanes.Gitleaks(exe, blobless, config, "HEAD~4..HEAD");
        t.True($"gitleaks on {names[4]} fails", result is not null && result.Detail.Contains("gitleaks logged an error", StringComparison.Ordinal),
            result is null ? "it passed" : result.Detail);
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Delete a folder and all in it, git's read-only object files included; what can't go stays.</summary>
    public static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A scratch folder under the temp folder; the OS clears what is left.
        }
    }
}
