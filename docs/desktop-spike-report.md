# Desktop spike report: is Avalonia the right framework for CFC Agent?

Written 2026-11-09, at the end of the spike (steps 0 to 8; the optional step 7, a tool-call display, was skipped).
Everything below was run on one machine: Windows 11 ARM64 running as a virtual machine on Apple silicon, 6 cores,
12.8 GB of memory, .NET 10, Avalonia 12.1.5. Nothing was run on macOS, Linux or x64.

**Updated 2026-11-15:** the open items below were decided, and the spike is closed. See
"Decisions taken after the spike". The rest of this report is unchanged and describes the spike as it ended.

## Decisions taken after the spike (2026-11-15)

Avalonia is the framework for the Windows product. The five items that were open before moving the spike into the
product were decided as follows:

| Open item | Decision |
|---|---|
| Mac and Linux never run | **Not tested for now.** The app is still unproven there. That run must happen before shipping to either. |
| A real change through the approval card | **Tested by hand** in the desktop app, by the user. No automated test writes to a database. |
| The app only finds the server in a development checkout | **The app will ship with the server.** Nothing is built for that yet. |
| Long conversations use too much memory | **Future development,** after the MVP: virtualize the message list (see the roadmap). |
| Small loose ends in the screens | **Fixed when the product's screens are built** from the modern look in `spikes/CFCAgent.UiShowcase`, which replaces the spike's screens. The loose ends: Allow changes and Retry stay clickable while Settings is open, the mode-change notice is styled as an error, and audit lines are not shown. |

**First step of the move, done 2026-11-15:** the logic layer is product code now. `CFCAgent.App.Core` moved from
`spikes/` to `src/`, its tests moved to `tests/CFCAgent.App.Core.Tests`, and both are part of `CFCAgent.sln`.

**Second step, done the same day:** the app itself is product code, with the modern look. `spikes/CFCAgent.App`
moved to `src/CFCAgent.App` and its window tests to `tests/CFCAgent.App.Tests`, and its three screens (chat,
approval card, settings) were restyled with the look from `spikes/CFCAgent.UiShowcase`: brown sidebar, green
accent, tan line under the top bar, light and dark. Only what the app really does was carried over. The
showcase's other pages and controls (the formulas dashboard, ingredients, customers, recent chats, new chat,
search, notifications, attachments, the model picker, suggestion chips, reply buttons and lookup tags) were left
out because nothing behind them exists yet; the showcase stays in `spikes/` as the reference for them.

The read-only / changes-allowed mode is now one switch inside the message box (off and green for read-only, on and
amber when changes are allowed). It replaces the badge and the separate button the spike had in its footer.

Flipping the switch still restarts the server and reconnects, because the tool list and the approval handshake
are fixed when a session connects. Since 2026-11-15 it keeps the conversation: the new session takes over every
earlier turn, uses the new mode's system prompt, and gets a short note for the model saying what changed. In the
spike a switch cleared the chat. Saving settings still starts a new conversation, because new settings can point
at a different database. Not yet tried with Claude itself (it needs an API key): in particular, going back to
read-only leaves earlier change-tool calls in the history while those tools are no longer offered.

Two of the loose ends were fixed in that step: a notice is now shown as information unless it reports a failure,
and the mode switch and Retry are not offered while Settings is open. Audit lines and the tool-call display are still
not shown. Measured after the restyle (Release, four runs, same machine): window in about 0.9 s, connected in about
1.6 s, 220 MB in use (104 MB private). That is about 0.2 s and 48 MB more than the spike's plain screens, numbers
below.

**New chat and recent chats, added 2026-11-15.** The sidebar has a New chat button and a list of recent chats.
New chat starts an empty conversation on the same connection; the server is not restarted. Every chat is saved
when a reply ends, to a SQLite file in the user's own profile (`%LOCALAPPDATA%\CFCAgent\conversations.db`),
not to the fertilizer database, so read-only mode still writes nothing there. A saved chat can be reopened, also
after the app was closed. Each one is kept in two forms, because they differ: what the window showed (notices,
approval records and stopped replies included) and what the model is sent (tool calls and their results
included). On reopening, the model gets the saved turns plus a note that time has passed, which mode the session
is in, and that figures should be looked up again. Deleting a chat takes two clicks.

Things to know about it:

* The file is not encrypted. It is as private as the user's other files. It holds what was asked and the data
  the replies and tool results showed (costs, customer names), but never a connection string.
* A chat reopened while connected to a different tenant setup says so, to the user and to the model.
* Tested on the stub model, and with a real tool result from the server going through the file unchanged. Not
  yet tried with Claude itself.
* Not built: searching chats, renaming them, and removing old ones automatically. The list shows the 50 most
  recent.
* It adds one package, Microsoft.Data.Sqlite (MIT), which brings SQLitePCLRaw (Apache-2.0) and SQLite itself
  (public domain). These are not in the licence count further down, which describes the spike as it ended.
* Startup and memory did not change (window in about 0.9 s, connected in about 1.6 s, 224 MB in use).

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
dotnet test tests/CFCAgent.App.Tests                                   # the window tests, with real clicks
CFCAGENT_BENCH=1 dotnet test tests/CFCAgent.App.Tests -c Release \
    --filter "FullyQualifiedName~LongConversation"                     # the long-conversation benchmark
dotnet test spikes/CFCAgent.Markdown.Bakeoff                           # the Markdown comparison (about 3 minutes)
dotnet build src/CFCAgent.App -c Release
spikes/tools/measure-startup.ps1 -Exe <path to CFCAgent.App.exe> -Label "Release" -Runs 5
CFCAGENT_LIVE_TESTS=1 dotnet test tests/CFCAgent.App.Core.Tests        # read-only checks against your real database
```

Screenshots and result files go to `CFCAGENT_SCREENSHOT_DIR` when it is set.
