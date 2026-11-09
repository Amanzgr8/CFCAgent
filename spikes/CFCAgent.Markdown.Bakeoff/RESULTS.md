# Markdown bake-off results (spike step 3)

Question: which free Markdown control should show the assistant's replies in the CFC Agent chat window?
Everything was run headlessly on Windows 11 ARM64 (native), Avalonia 12.1.5, .NET 10, Debug build. Re-run with
`dotnet test` in this folder; pictures and a results file land in `bin/.../screenshots` (or in
`CFCAGENT_SCREENSHOT_DIR`).

## Candidates

| Library | Version | Licence | Avalonia | Streaming support | Notes |
|---|---|---|---|---|---|
| MarkView.Avalonia | 12.4.0 | MIT | 12 (version number follows Avalonia) | Yes: `BeginStreaming()` session, redraws only the end, closes unfinished syntax, 32 ms update interval | Core needs only Avalonia + Markdig. Has a chat theme. Optional add-on packages for code colouring, SVG, Mermaid, math. |
| LiveMarkdown.Avalonia | 2.5.1 | Apache-2.0 | 12 | Yes: `ObservableStringBuilder` | Brings TextMateSharp + Onigwrap (native library for every platform). Apache-2.0 means a NOTICE entry. |
| Markdown.Avalonia | 12.0.2 | MIT | 12 | No: the whole text must be set again for every piece | Long-standing library. Pulls AvaloniaEdit, HtmlAgilityPack, Svg. |
| LM.Markdown.Avalonia | 1.0.1 | MIT | **11.3 only** | n/a | Dropped: not built for Avalonia 12. |

## The test reply

`sample-reply.md`: a heading, bold/italic/inline code, a 50-row table, a bullet list with a nested numbered list,
a block quote, a SQL code block, and a closing line (about 3.7 KB). Shown in a 760 px wide window.

## Looks (see the pictures)

* **MarkView**: everything rendered. Full-width table with header shading and row lines, code block in a card with a
  copy button, quote bar, lists. Looked best of the three with no styling work.
* **LiveMarkdown**: everything rendered. Table does not fill the width, inline code and the code block use a dark
  TextMate colour scheme that looks heavy on a light page (fixable with styling), quote is a dark band.
* **Markdown.Avalonia**: rendered but the code block showed only its language label and no code in the headless run;
  headings and table borders look dated.
* While a table is half-written, MarkView and LiveMarkdown both showed the finished rows cleanly (no stray pipes).

## Speed

Whole reply handed over at once, time until the last words were on screen: plain text box 44 ms (floor),
MarkView 53 ms, Markdown.Avalonia 80 ms, LiveMarkdown 84 ms.

Streaming, 858 pieces of 1-8 characters, one piece every ~15 ms (a fast model). The harness itself costs a lot
(a plain text box scored 36 ms of processor time per piece, from the timer loop and drawing a tall window), so
only the **differences from the baseline** mean anything:

| | Wall time | Processor time per piece | Median time per piece (15 is the wait) |
|---|---|---|---|
| Plain text box (baseline) | 39 s | 36 ms | 43 ms |
| MarkView | 39 s | 32 ms | 41 ms |
| LiveMarkdown | 47 s | 40 ms | 52 ms |
| Markdown.Avalonia | 78 s | 88 ms | 85 ms (worst 315) |

MarkView keeps up with the model at baseline cost. LiveMarkdown costs about 10% more and falls behind a little.
Markdown.Avalonia redraws everything every piece, so it gets slower as the reply grows and would visibly lag on
long answers.

## Decision

**MarkView.Avalonia.** Best looks out of the box, built for streaming chat, smallest dependency set, MIT.
It is already wired into the spike chat window (`MarkdownMessage.cs`, one viewer per assistant message,
`ChatTheme`), covered by `CFCAgent.App.Tests/MarkdownReplyTests.cs`.

## Not covered (still open)

* Only run headless on Windows ARM64; never on a real GPU, x64, macOS or Linux.
* Dark theme, links, selecting text across a table, very long replies (hundreds of rows), and many viewers in a
  long conversation (MarkView renders lazily, but this was not measured).
* Single maintainer on MarkView and a young project: pinned to `12.4.*`; the fallback is LiveMarkdown, whose
  wrapper (`LiveMarkdownHost` in this project) already works.
* MIT notice for MarkView and its dependencies (Markdig is BSD-2-Clause) must go in the third-party notices file
  before shipping.
