# Desktop spike report: is Avalonia the right framework for CFC Agent?

Written 2026-11-09, at the end of the spike (steps 0 to 8; the optional step 7, a tool-call display, was skipped).
Everything below was run on one machine: Windows 11 ARM64 running as a virtual machine on Apple silicon, 6 cores,
12.8 GB of memory, .NET 10, Avalonia 12.1.5. Nothing was run on macOS, Linux or x64.

## Verdict

**Avalonia passed every test the spike set for it on Windows, and nothing it showed us argues for switching to
another framework. The decision is not final yet: the cross-platform half of the question is still open, because
the app has never been run on a Mac or a Linux machine.**

The roadmap's spike asked for three things, and set one condition:

| Asked for | Result |
|---|---|
| A window showing a streamed reply with a Markdown table | Done. MarkView.Avalonia renders it, formatted, while it streams. |
| An approval step that answers the backend's `ConfirmAsync` | Done, as an inline card in the chat (first built as a dialog, then replaced at your request). |
| The real MCP server launched as a child process | Done. Read-only by default, ends cleanly on close and on a forced kill. |
| "If rendering the reply proves painful, revisit Blazor Hybrid or Uno" | It did not. Markdown was the biggest unknown and it works. |
| Run on Windows ARM64 first, then a Mac and a Linux machine | Windows ARM64 done. Mac and Linux not done. |

## What was built

An Avalonia app in `spikes/` with the screens the product needs: a chat window with streamed Markdown replies, a
connection to the real backend through the tenant setup, a read-only / changes-allowed switch with an approval card for
every change, and a settings screen that saves the connection string to the operating system's credential store.
The window holds no logic: everything that decides behaviour lives in `CFCAgent.App.Core`, which has no UI-framework
reference. If the framework ever changes, that layer, and its 75 tests, carry over.

Size of the work: about 1,800 lines for the app and its logic, 2,600 lines of tests for it.

## Measurements

Release build, five runs each, on the machine described above. Times are from process start.

| What | Result |
|---|---|
| Window ready (warm) | about 0.75 s |
| Window ready (first run after a build) | 2.5 s |
| Connected to the real database through the server (warm) | about 1.3 s |
| Connected (first run after a build) | 3.6 s |
| Memory, idle, UI only | 140 to 149 MB working set (73 MB private) |
| Memory, idle, connected | 172 to 174 MB working set (77 to 78 MB private), plus the server process at about 10 MB |
| Normal close: server stopped | within 35 ms of the app starting to exit; no process left behind |
| App force-killed: server stopped | within 127 ms; no process left behind |
| Streaming a reply into a window already holding 100 table-heavy replies | frame time median 0 ms, 95th percentile 14 ms, worst 142 ms |

**Publish sizes** (not trimmed or compressed):

| Package | Size |
|---|---|
| App, Windows ARM64, needs .NET installed | 143 MB, of which 101 MB is debug symbols, so about 42 MB to ship |
| App, Windows ARM64, self-contained | 229 MB, about 128 MB without symbols |
| App, Linux x64 / Linux ARM64 | 38 MB each (needs .NET installed) |
| App, macOS ARM64 / macOS x64 | 44 MB each (needs .NET installed) |
| MCP server, Linux x64 / macOS ARM64 | 5 MB each (needs .NET installed) |
| MCP server, Windows ARM64, self-contained | 92 MB |

The app and the server **compile and publish for Linux x64, Linux ARM64, macOS ARM64, macOS x64, Windows x64 and
Windows ARM64** without changes. That shows the code is portable; it does not show that it runs correctly there.

## Findings that matter

1. **A long conversation of table-heavy replies uses a lot of memory.** Every reply with a 50-row table costs about
   6 MB of managed memory (about 660 on-screen elements). Ten such replies: 70 MB. Fifty: 310 MB. A hundred: 620 MB,
   and laying out a hundred at once took 4.8 s. Streaming stayed smooth throughout. Ordinary short replies cost far
   less, so this bites only for long sessions full of big tables. The message list is not virtualized (every message
   stays alive on screen), which is the likely fix: virtualize the list, or collapse old replies. Neither was tried.
2. **Rendering the reply was not painful.** MarkView.Avalonia (MIT) handles tables, lists, code and half-finished
   Markdown while streaming, at no more cost than a plain text box. Full numbers are in
   `spikes/CFCAgent.Markdown.Bakeoff/RESULTS.md`.
3. **The UI can be built and checked without a designer.** Screens were checked with Avalonia's free headless mode:
   real mouse clicks and key presses on the real window, plus screenshots. This is what caught the bug where Enter
   did not send (the text box swallowed the key). It also made the approval card testable down to "Enter can only
   cancel".
4. **The approval card is the right shape for changes.** Cancel has the focus, Enter and Esc cancel, only a deliberate
   click on Apply approves, the first answer wins, a question that is no longer wanted is withdrawn, and the answered
   card stays in the chat as a record. No test writes to a database; the live change is still your hands-on check.
5. **The connection string is handled carefully.** It is typed into a masked box, goes to the credential store, is
   never shown back, and every message the app shows about a connection is scrubbed of it first.

## Not covered by this spike

* **macOS and Linux.** Not run, not even launched. This is the main open item. macOS also needs the bundled server
  signed and notarized (nested binaries first), which has not been tried.
* **x64 Windows, a real GPU, high-DPI screens, dark theme, screen readers.**
* **A real change through the approval card** with a model calling a write tool, and **Claude tool calls in the
  window** (needs your API key). The tool-call display (step 7) was skipped.
* **Installer, auto-update, and shipping the server with the app.** Today the app finds the server by walking up to
  `CFCAgent.sln`, which only works in a development checkout.
* **Long-conversation fix** (finding 1).

## Risks

| Risk | Mitigation |
|---|---|
| Avalonia 12 is young; the company is small | The logic is framework-free, so a switch costs the views only, not the behaviour or the tests. |
| MarkView.Avalonia is young with one maintainer | Pin `12.4.*`. LiveMarkdown.Avalonia was benchmarked as a fallback and works. |
| Avalonia's designer, Hot Reload and DevTools are paid for commercial use (Plus, about 299 euros a year per developer); the free Community tier is non-commercial only | Work without them, as this spike did. Revisit only if building screens gets slow. |
| Memory grows with long, table-heavy conversations | Virtualize the message list; test with the same benchmark. |
| macOS signing and notarization of the bundled server | Try it early, on a Mac, before committing to dates. |

## Licences

69 NuGet packages in the app and server together: 63 MIT, 3 Apache-2.0 (Anthropic SDK and the MCP SDK), 1 BSD-2-Clause
(Markdig), 1 BSD-3-style (the ANGLE graphics layer, licence file in its package), and 1 PostgreSQL licence (Npgsql,
permissive). No copyleft and nothing paid. A third-party notices file listing these is still to be written before
anything ships.

## Recommendation

1. **Keep Avalonia as the front-runner and confirm it on a Mac.** Run the app on your Mac itself (the Windows machine
   here is a virtual machine on one) and on a Linux machine or VM. Launching it, connecting, streaming a reply and
   closing it is enough to decide. If that works, make Avalonia the framework.
2. **Then fix the long-conversation memory** by virtualizing the message list, and re-run the benchmark.
3. **Do your hands-on change check** (the D066 no-op through the approval card) before anyone relies on write mode.
4. Then the product work: shipping the server with the app, installer and updates, macOS signing, the third-party
   notices file, and the tool-call display.

## How to reproduce

```
dotnet test spikes/CFCAgent.App.Tests                                  # the window tests, with real clicks
CFCAGENT_BENCH=1 dotnet test spikes/CFCAgent.App.Tests -c Release \
    --filter "FullyQualifiedName~LongConversation"                     # the long-conversation benchmark
dotnet test spikes/CFCAgent.Markdown.Bakeoff                           # the Markdown comparison (about 3 minutes)
dotnet build spikes/CFCAgent.App -c Release
spikes/tools/measure-startup.ps1 -Exe <path to CFCAgent.App.exe> -Label "Release" -Runs 5
CFCAGENT_LIVE_TESTS=1 dotnet test spikes/CFCAgent.App.Core.Tests       # read-only checks against your real database
```

Screenshots and result files go to `CFCAGENT_SCREENSHOT_DIR` when it is set.
