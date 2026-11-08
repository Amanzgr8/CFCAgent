"""Regenerates CFC_AI_Assistant_Roadmap.pdf. Edit the PHASES / OPEN / RISKS / FUTURE data below and re-run:

    py docs/build_roadmap_pdf.py

Requires reportlab (pip install reportlab). Output path is the Desktop "Fertilizer Chatbot" folder.
Text goes through reportlab's Paragraph parser: escape & as &amp; and use <b>..</b> for bold.
"""
from reportlab.lib.colors import Color
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.platypus import KeepTogether, ListFlowable, ListItem, Paragraph, SimpleDocTemplate, Spacer

OUT = r"C:\Users\amanu\Desktop\Fertilizer Chatbot\CFC_AI_Assistant_Roadmap.pdf"
UPDATED = "2026-11-08"

DARK = Color(0.101961, 0.101961, 0.101961)
STATUS_COLORS = {
    "Done": Color(0.13, 0.45, 0.22),
    "Partly done": Color(0.70, 0.45, 0.05),
    "Not started": Color(0.40, 0.40, 0.40),
}

base = getSampleStyleSheet()["Normal"]
title_style = ParagraphStyle("T", parent=base, fontName="Helvetica-Bold", fontSize=20, textColor=DARK, leading=24, spaceAfter=6)
body_style = ParagraphStyle("B", parent=base, fontName="Helvetica", fontSize=9.5, textColor=DARK, leading=13, alignment=TA_LEFT, spaceAfter=6)
h_style = ParagraphStyle("H", parent=base, fontName="Helvetica-Bold", fontSize=13, textColor=DARK, leading=16, spaceBefore=10, spaceAfter=2)
sub_style = ParagraphStyle("S", parent=base, fontName="Helvetica-Bold", fontSize=10.5, textColor=DARK, leading=13, spaceBefore=8, spaceAfter=3)
bullet_style = ParagraphStyle("L", parent=body_style, spaceAfter=3)


def status_line(status: str) -> Paragraph:
    color = STATUS_COLORS[status].hexval()[2:]
    return Paragraph(f'<font name="Helvetica-Bold" size="9" color="#{color}">[{status}]</font>', body_style)


def bullets(items):
    return ListFlowable(
        [ListItem(Paragraph(text, bullet_style)) for text in items],
        bulletType="bullet", start="circle", leftIndent=16, bulletFontSize=6,
    )


INTRO = (
    "Implementation plan and status, turning the validated proof of concept into a standalone product. "
    "For how each piece is built, see the companion CFC AI Assistant \u2014 Architecture document. Phased so CFC "
    "Fertilizer is proven end-to-end before paying the cost of proving reuse on a second product; "
    "licensing/auth is built last, as decided."
)

PHASES = [
    ("Phase 0 \u2014 Carried over from the proof of concept", "Done", [
        "CFC_Schema_Map.md \u2014 table/column meanings, joins, dead/legacy columns to ignore.",
        "CFC_Assistant_Instructions.md \u2014 the system prompt (read-only behavior, units, stale-solution and infeasibility handling).",
        "CFC_Assistant_Test_Questions.md \u2014 27 scored questions, all answered correctly against the live read-only connection.",
    ]),
    ("Phase 1 \u2014 Custom MCP server: read tools", "Done", [
        "MCP server stood up on the official MCP C# SDK.",
        "Schema map formalized into schema-map.yaml, consumed by a generic read-tool engine (no C# per tool).",
        "13 read tools covering ingredients, nutrients, formulas, customers, and system settings.",
        "Doc tools (search_docs, read_section) over a reformatted guide.md (17 addressable sections).",
        "instructions.md plus the schema ground rules served as the MCP server's own live instructions.",
        "27-question set re-run against the new server by a blind-tested agent restricted to only these tools: 26/27 on the first pass, 27/27 after wiring in instructions.md.",
    ]),
    ("Phase 2.1 \u2014 Simple write tools", "Done", [
        "Generic, config-driven write engine: one table, one or more business keys, a bounded set of writable columns. Keys can be composite and are resolved from codes the user knows to internal IDs, which are never exposed.",
        "Every write previews first (reads current values, reports the diff); only confirm=true applies it, inside a transaction, with an audit line to stderr.",
        "Six tools: update_ingredient (cost, active, in-stock, binding-agent flag), update_customer (name, address, phone, active), update_nutrient (name, active), update_formula (non-solution fields), update_formula_nutrient_limits, update_formula_ingredient_limits.",
        "Verified live: each kind of write applied, checked against the database, and reverted. Error paths (nonexistent codes, no fields supplied) reject cleanly with nothing written.",
        "The database user now has read and write privileges (a new user replaced mcp_readonly for the server).",
    ]),
    ("Phase 2.2 \u2014 Complex write tools and the optimizer", "Partly done", [
        "<b>Done:</b> set_ingredient_binder, hand-written because it validates that the chosen binder is an active, binder-flagged ingredient. Touches only the ingredient's own binder-config columns, never solver output. Clearing a binder writes 0, matching how the CFC app stores it.",
        "<b>Decision:</b> the assistant does not call CFC's solver. It says what would be affected and tells the user to change the input and click Formulate; it never computes a recomputed cost itself. Whether the solver (LpEngine.DoLP, called from Product.cs) can be run outside the UI stays an optional upside, deferred.",
        "<b>Built in Phase 3:</b> the MCP elicitation confirmation framework \u2014 it needed our own client. The server now enforces it on every write tool (see Phase 3).",
        "System-wide changes (e.g. switching measurement units) stay read/advisory-only, never a write tool.",
        "<b>Not yet run:</b> the test that an inactive ingredient is rejected as a binder.",
    ]),
    ("Phase 3 \u2014 Chatbot backend", "Partly done", [
        "<b>Done:</b> a new, separate project (CFCAgent.Backend) that holds the conversation, calls Claude through IChatClient, and acts as an MCP client launching the MCP server over stdio, plus a console host (CFCAgent.Backend.Cli) to test it without any UI. The project is renamed from CFCChatBot to CFC Agent.",
        "<b>Done:</b> the CLI has --list-tools, --call (read tools only), --write (runs one write tool by hand, with the real console prompt) and --read-only (hides every tool the server does not declare read-only, so a model session cannot change data).",
        "<b>Done:</b> server-side elicitation. A write call with confirm=true is only a request: the server asks the client to show the exact preview and writes only on an accepted form with an explicit true. A client that cannot prompt, a decline, a cancel, a timeout or any error means nothing is written. Applies to all 7 write tools (the 6 schema-map tools and set_ingredient_binder).",
        "<b>Tests:</b> unit tests of the approval rule, and end-to-end tests that drive the real server with scripted answers (preview never asks; decline, cancel and no-prompt-support write nothing). One test that approves, applies and restores a real change is skipped unless explicitly enabled, and is still to be run by hand.",
        "<b>Not yet done:</b> the live Claude conversation through the backend (needs an API key in the user's own environment); loading product and tenant config at session start, replacing today's two environment variables; the manual approve-and-restore check in the CLI.",
        "<b>Consequence:</b> the Desktop app's Code tab declines the prompt, so write tools called from there are now blocked. Writes work through our own client and Claude Code in a terminal.",
    ]),
    ("Phase 4 \u2014 Desktop frontend", "Not started", [
        "Chat window, message history, tool-call/result display, and confirmation dialogs for write actions.",
        "Wired to the local backend over local IPC/HTTP, running alongside the app on the customer's machine.",
    ]),
    ("Phase 5 \u2014 Internal pilot", "Not started", [
        "Deploy to a handful of internal users (formulators, plant staff, sales) against a non-production database.",
        "Re-run and expand the test-question set; log real questions users ask that weren't anticipated.",
        "Iterate on instructions, schema map, and tools based on what's wrong \u2014 expect 2-3 rounds.",
    ]),
    ("Phase 6 \u2014 Prove reuse on a second product", "Not started", [
        "Pick a second formulation product, ideally SQL-Server-backed, to validate the multi-engine driver abstraction for real.",
        "Write its product config (schema-map.yaml, instructions, docs/, db_engine: mssql); its simple writes reuse the generic field-update tool through config alone.",
        "Write that product's complex write tools only if it has any \u2014 the one piece this phase doesn't get for free from config.",
        "Confirm the claim the whole design rests on: simple reuse is a config change with zero backend/frontend code changes.",
    ]),
    ("Phase 7 \u2014 Licensing and user auth", "Not started", [
        "Integrate with the existing CFC license server under a new SoftwareID, reusing LicenseService.cs / UpdaterClient.",
        "Add end-user login within an install (reusing a cfcuser-style scheme or a new lightweight one).",
        "Decide installer packaging: bundled with the CFC Fertilizer installer, or standalone.",
    ]),
]

OPEN = [
    "Whether to use an ORM/query builder (Dapper, EF Core) in the MCP server's data layer, versus Npgsql/Microsoft.Data.SqlClient directly.",
    "Whether to split database access into a read-only user for the read tools and a separate write user for the write tools. Today everything runs as one read/write user, so the database no longer stops a read tool from writing; only the code does.",
    "The exact end-user authentication scheme inside a single install (Phase 7).",
    "Installer/packaging strategy: bundled with CFC Fertilizer vs. a standalone installer (Phase 7).",
]

RISKS = [
    "<b>Solver integration.</b> Lower risk than first thought: the accepted fallback (tell the user to click Formulate) works today. Calling the solver directly would only add live re-formulated answers.",
    "<b>Data caveats found during the POC.</b> formulationstatus = 1 doesn't reliably mean \"not formulated\" for migrated data, and duplicate customer records exist (e.g. two separate \"KT Farms\" records). Both are encoded in schema-map.yaml's ground rules and tool descriptions.",
    "<b>Quality doesn't transfer automatically</b> across model swaps or across products \u2014 re-run the test-question set after any change to the model, the instructions, or a product config.",
]

DEV_NOTES = [
    "Register cfc-fertilizer-dev <b>once</b>, at user scope. A second copy in claude_desktop_config.json, without the connection string, caused a long-running failure: tools present in both copies were routed to the one that couldn't reach the database.",
    "Changes to an existing tool's code take effect after a rebuild. Adding a tool or changing the tool list needs an app restart before a session sees it.",
    "The MCP library hides any unexpected server-side exception behind a generic \"An error occurred invoking\" message. When a tool fails with no detail, surface the real exception first.",
]

FUTURE = [
    ("Write-tool confirmation: now enforced by the server (remaining gaps)", [
        "Until 2026-11-08 the confirm parameter was just another argument in the call, so nothing stopped a model from applying a write on its first call. The server now asks the client to prompt the human directly, outside the model's context, and writes only on an explicit yes. What remains: the approve path has only been exercised by an opt-in test that is still to be run by hand, and the standalone app's own confirmation dialog does not exist until Phase 4.",
    ], [
        "<b>MCP elicitation \u2014 built.</b> The server asks the client to prompt the human directly, outside the model's context, and waits for the answer.",
        "<b>What we found (probe run 2026-11-02):</b> Claude Code tells the server it supports elicitation (form and URL modes). In a terminal session the dialog appears and the answer returns to the server. In the Desktop app's Code tab it declines local requests without showing anything, so it can't gate writes there.",
        "<b>Required design:</b> the write proceeds only when the response is an accept with an explicit true. A decline, a cancel, a missing value, or a client without elicitation support all block the write. In the terminal test, an accepted form with the checkbox unticked came back with no value, so a missing value must never count as yes.",
        "<b>Preview token (minor, defense-in-depth only).</b> It forces two round trips but still returns through the channel the model reads \u2014 not a substitute for elicitation.",
    ]),
    ("Audit trail is log lines, not a durable record", [
        "Every applied write logs a line to stderr (timestamp, tool, key, old/new values). The client only keeps a server's stderr from startup, so these lines are effectively lost. Before customer use this needs a real store (a small audit table in Postgres, or a per-install log file the desktop app owns) plus a per-user identity, once Phase 7's auth lands.",
    ], []),
    ("The MCP server has no credential store of its own", [
        "The server reads one full connection string, password included, from an environment variable. Both the bundled Postgres connector (OS keyring) and the CFC Fertilizer app itself (DPAPI-encrypted, PostgresMachineConfig.cs) already solve this properly; the server should adopt the same pattern.",
    ], []),
    ("Some rules are instructions, not code", [
        "Total Nitrogen (1), Phosphate (6), and Potassium (7) must stay active, but only instructions.md says so: update_nutrient itself would apply the change. Enforce it in the tool.",
    ], []),
    ("The server only works with PostgreSQL (SQL Server support is planned for Phase 6)", [
        "The MCP layer, the preview-and-approve flow, the instructions, the backend and the CLI are engine-neutral, but the data access is not. Program.cs creates an NpgsqlDataSource, and the read engine, the write engine and the binder tool all open Npgsql connections directly. The SQL in schema-map.yaml uses PostgreSQL syntax: ILIKE for case-insensitive search, LIMIT @limit, and || to join strings. A Microsoft SQL Server product would need different syntax for each.",
    ], [
        "<b>Put the database behind a small abstraction:</b> open a connection, start a transaction, bind parameters, using System.Data.Common with Npgsql and Microsoft.Data.SqlClient as the two drivers.",
        "<b>Let each product's schema map name its engine</b>, and add a small dialect layer for row limits, case-insensitive matching and string concatenation. Boolean handling (smallint, tested with <> 0) and identifier quoting differ by engine too.",
        "<b>Write the SQL per product and engine.</b> A SQL Server product would ship its own schema map. This is likely the largest part of the work.",
        "<b>Do it with a real second product in Phase 6</b>, not ahead of time. An abstraction designed without a real SQL Server database to test against tends to be wrong. Until then, keep SQL in the schema map and out of C#. The one exception is the binder tool's hand-written SQL, which is plain and should port easily but needs checking.",
    ]),
]


def build():
    story = [
        Paragraph("CFC AI Assistant \u2014 Roadmap", title_style),
        Paragraph(f"Last updated {UPDATED}", body_style),
        Paragraph(INTRO, body_style),
    ]

    for title, status, items in PHASES:
        story.append(KeepTogether([Paragraph(title, h_style), status_line(status), bullets(items[:1])]))
        if len(items) > 1:
            story.append(bullets(items[1:]))

    story += [Paragraph("Open decisions", h_style), Paragraph("Not yet settled, and not blocking the plan above:", body_style), bullets(OPEN)]
    story += [Paragraph("Key risks to watch", h_style), bullets(RISKS)]
    story += [Paragraph("Development environment notes", h_style), bullets(DEV_NOTES)]

    story += [
        Paragraph("Future development / improvements", h_style),
        Paragraph("Not required for the MVP as scoped, but real gaps worth closing before write tools reach users outside this development setup.", body_style),
    ]
    for heading, paragraphs, items in FUTURE:
        block = [Paragraph(heading, sub_style)] + [Paragraph(p, body_style) for p in paragraphs]
        story.append(KeepTogether(block))
        if items:
            story.append(bullets(items))

    SimpleDocTemplate(OUT, pagesize=letter, leftMargin=60, rightMargin=61.46, topMargin=56, bottomMargin=56,
                      title="CFC AI Assistant - Roadmap").build(story)
    print("wrote", OUT)


if __name__ == "__main__":
    build()
