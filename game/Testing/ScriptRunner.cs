using System.Collections.Generic;
using System.Globalization;
using Godot;
using Strata.Core.Commands;

namespace Strata.Game.Testing;

/// <summary>
/// Runs command scripts (.cmds) against the live game, one line per frame, with engine-time directives:
/// <c>wait &lt;seconds&gt;</c>, <c>wait_until &lt;probe&gt; &lt;op&gt; &lt;value&gt; [timeout]</c>,
/// <c>hold &lt;action&gt; &lt;seconds&gt;</c> (simulated input) and <c>repeat &lt;n&gt; &lt;command…&gt;</c>.
/// Any failing command or assertion fails the script. Prints <c>SMOKE PASS</c>/<c>SMOKE FAIL</c> and can quit.
/// </summary>
public sealed partial class ScriptRunner : Node
{
    private readonly Queue<(int Line, string Text)> _lines = new();
    private CommandRegistry _registry = null!;
    private CommandContext _ctx = null!;
    private double _waitLeft;
    private (string Probe, string Op, string Value, double Timeout)? _waitUntil;
    private double _waitElapsed;
    private string? _heldAction;
    private double _holdLeft;
    private int _failures;
    private string _name = "";

    public bool QuitWhenDone { get; set; } = true;

    public event System.Action<bool>? Finished;

    public void Start(string scriptPath, string text, CommandRegistry registry, CommandContext ctx)
    {
        _registry = registry;
        _ctx = ctx;
        _name = scriptPath;
        int n = 0;
        foreach (var raw in text.Split('\n'))
        {
            n++;
            var line = raw.Trim();
            if (line.Length > 0 && !line.StartsWith('#')) _lines.Enqueue((n, line));
        }
        GD.Print($"[script] {_name}: {_lines.Count} steps");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_heldAction != null)
        {
            _holdLeft -= delta;
            if (_holdLeft > 0) return;
            Input.ActionRelease(_heldAction);
            _heldAction = null;
        }
        if (_waitLeft > 0)
        {
            _waitLeft -= delta;
            return;
        }
        if (_waitUntil is { } w)
        {
            _waitElapsed += delta;
            var check = _registry.Execute(_ctx, $"assert {w.Probe} {w.Op} {w.Value}");
            if (check.Ok)
            {
                _waitUntil = null;
            }
            else if (_waitElapsed > w.Timeout)
            {
                Fail(0, $"wait_until timed out after {w.Timeout:0}s: {check.Output}");
                _waitUntil = null;
            }
            return;
        }
        if (_lines.Count == 0)
        {
            Finish();
            return;
        }
        var (lineNo, text) = _lines.Dequeue();
        RunLine(lineNo, text);
    }

    private void RunLine(int lineNo, string text)
    {
        var tokens = CommandRegistry.Tokenize(text);
        switch (tokens[0])
        {
            case "wait":
                _waitLeft = Seconds(tokens, 1, lineNo);
                return;
            case "wait_until" when tokens.Count >= 4:
                _waitUntil = (tokens[1], tokens[2], tokens[3], tokens.Count > 4 ? Seconds(tokens, 4, lineNo) : 60);
                _waitElapsed = 0;
                return;
            case "hold" when tokens.Count >= 3:
                _heldAction = tokens[1];
                if (!InputMap.HasAction(_heldAction))
                {
                    Fail(lineNo, $"unknown input action '{_heldAction}'");
                    _heldAction = null;
                    return;
                }
                Input.ActionPress(_heldAction);
                _holdLeft = Seconds(tokens, 2, lineNo);
                return;
            case "press" or "release" when tokens.Count >= 2:
                // Keeps an action held (or lets go: release, or press … off) while the script goes on, e.g. to
                // screenshot mid-flight.
                if (!InputMap.HasAction(tokens[1])) Fail(lineNo, $"unknown input action '{tokens[1]}'");
                else if (tokens[0] == "press" && !(tokens.Count > 2 && tokens[2] == "off")) Input.ActionPress(tokens[1]);
                else Input.ActionRelease(tokens[1]);
                return;
            case "repeat" when tokens.Count >= 3 && int.TryParse(tokens[1], out int times):
                var body = string.Join(' ', tokens.GetRange(2, tokens.Count - 2));
                for (int i = 0; i < times; i++) Execute(lineNo, body);
                return;
        }
        Execute(lineNo, text);
    }

    private void Execute(int lineNo, string text)
    {
        var result = _registry.Execute(_ctx, text);
        GD.Print($"[script] {lineNo}: {text} -> {(result.Ok ? "" : "FAILED ")}{result.Output}");
        if (!result.Ok) Fail(lineNo, result.Output);
    }

    private double Seconds(List<string> tokens, int i, int lineNo)
    {
        if (i < tokens.Count && double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) return s;
        Fail(lineNo, "expected a number of seconds");
        return 0;
    }

    private void Fail(int lineNo, string message)
    {
        _failures++;
        GD.PrintErr($"[script] {_name}:{lineNo}: {message}");
    }

    private void Finish()
    {
        SetPhysicsProcess(false);
        bool pass = _failures == 0;
        string kind = QuitWhenDone ? "SMOKE" : "SCRIPT";
        GD.Print(pass ? $"{kind} PASS {_name}" : $"{kind} FAIL {_name} ({_failures} failures)");
        Finished?.Invoke(pass);
        if (QuitWhenDone) GetTree().Quit(pass ? 0 : 1);
    }
}
