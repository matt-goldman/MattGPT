#!/usr/bin/env python3
"""
Stream through ChatGPT export JSON files and collect comprehensive statistics
about content types, author roles, metadata fields, rich media, etc.

Uses ijson for streaming to handle the ~141 MB main export file without
loading the entire document into memory.

Usage:
    python tools/analyse-export.py
"""

import ijson
import json
import os
import sys
import glob
from collections import Counter, defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent


# ══════════════════════════════════════════════════════════════════════════════
# Accumulators
# ══════════════════════════════════════════════════════════════════════════════

total_conversations = 0
total_messages = 0
total_nodes = 0
null_message_nodes = 0

content_types = Counter()
author_roles = Counter()
author_names = Counter()         # only non-null
recipient_values = Counter()
channel_values = Counter()
model_slugs = Counter()          # from message metadata
default_model_slugs = Counter()  # conversation-level
status_values = Counter()
gizmo_ids = Counter()            # conversation.gizmo_id
gizmo_types = Counter()

# content_type → first example (truncated raw JSON)
content_type_examples: dict[str, str] = {}

# message.metadata field presence (field → count of messages where non-null)
msg_metadata_fields = Counter()
# content-level fields
content_fields = Counter()
# conversation-level field presence
conv_field_presence = Counter()

# image asset pointers
image_asset_pointers = 0
image_asset_schemes = Counter()
image_asset_metadata_keys = Counter()

# parts analysis (inside text / multimodal_text)
multimodal_part_types = Counter()

# attachments
attachment_count = 0
attachment_mime_types = Counter()
attachment_sources = Counter()
attachment_examples: list[dict] = []

# citations
citation_count = 0
citation_format_types = Counter()

# content references
content_ref_count = 0
content_ref_types = Counter()

# aggregate_result (code interpreter)
aggregate_result_count = 0

# canvas
canvas_count = 0
canvas_types = Counter()

# reasoning
reasoning_recap_count = 0
thoughts_count = 0

# search
search_result_group_count = 0

# dictation / voice
dictation_count = 0

# async tasks (deep research)
async_task_count = 0
async_task_types = Counter()

# computer output (operator)
computer_output_count = 0

# user_editable_context (custom instructions)
user_editable_context_count = 0

# memory scope
memory_scope_values = Counter()

# weight values
weight_values = Counter()

# tether quote URL schemes
tether_url_schemes = Counter()

# system_error names
system_error_names = Counter()

# citable_code_output
citable_code_output_count = 0

# message recipient → author.name cross-tab (tool dispatch)
tool_dispatch = Counter()  # "recipient → author.name"

# conversation_template_id values
template_ids = Counter()


# ══════════════════════════════════════════════════════════════════════════════
# Helpers
# ══════════════════════════════════════════════════════════════════════════════

def safe_str(val) -> str:
    if val is None:
        return "(null)"
    return str(val)


def track_keys(obj: dict, counter: Counter):
    """Increment counter for each key whose value is not None."""
    if not isinstance(obj, dict):
        return
    for k, v in obj.items():
        if v is not None:
            counter[k] += 1


def truncate(s: str, n: int = 600) -> str:
    return s[:n] + "..." if len(s) > n else s


# ══════════════════════════════════════════════════════════════════════════════
# Analysis functions
# ══════════════════════════════════════════════════════════════════════════════

def analyse_content(content: dict):
    global image_asset_pointers, user_editable_context_count
    global reasoning_recap_count, thoughts_count, computer_output_count
    global citable_code_output_count

    ct = content.get("content_type", "(missing)")
    content_types[ct] += 1
    track_keys(content, content_fields)

    # Capture first example
    if ct not in content_type_examples:
        try:
            raw = json.dumps(content, ensure_ascii=False)
            content_type_examples[ct] = truncate(raw)
        except Exception:
            content_type_examples[ct] = "(serialisation error)"

    # Track specific types
    if ct == "user_editable_context":
        user_editable_context_count += 1
    elif ct == "reasoning_recap":
        reasoning_recap_count += 1
    elif ct == "thoughts":
        thoughts_count += 1
    elif ct == "computer_output":
        computer_output_count += 1
    elif ct == "citable_code_output":
        citable_code_output_count += 1
    elif ct == "system_error":
        system_error_names[safe_str(content.get("name"))] += 1
    elif ct == "tether_quote":
        url = content.get("url", "") or ""
        if "://" in url:
            tether_url_schemes[url.split("://")[0]] += 1
        elif url.startswith("file-"):
            tether_url_schemes["file-id"] += 1
        else:
            tether_url_schemes["(other)"] += 1

    # Parts analysis
    parts = content.get("parts")
    if isinstance(parts, list):
        for part in parts:
            if isinstance(part, str):
                multimodal_part_types["string"] += 1
            elif isinstance(part, dict):
                pct = part.get("content_type", "(unknown_object)")
                multimodal_part_types[safe_str(pct)] += 1

                if pct == "image_asset_pointer":
                    image_asset_pointers += 1
                    ap = part.get("asset_pointer", "")
                    if "://" in ap:
                        image_asset_schemes[ap.split("://")[0]] += 1
                    else:
                        image_asset_schemes["(no_scheme)"] += 1
                    # Track metadata keys on image assets
                    meta = part.get("metadata")
                    if isinstance(meta, dict):
                        track_keys(meta, image_asset_metadata_keys)
            elif part is None:
                multimodal_part_types["(null)"] += 1
            else:
                multimodal_part_types[f"(json_{type(part).__name__})"] += 1


def analyse_message_metadata(metadata: dict):
    global attachment_count, citation_count, content_ref_count
    global aggregate_result_count, canvas_count, search_result_group_count
    global dictation_count, async_task_count

    track_keys(metadata, msg_metadata_fields)

    ms = metadata.get("model_slug")
    if ms:
        model_slugs[ms] += 1

    # Attachments
    atts = metadata.get("attachments")
    if isinstance(atts, list):
        for a in atts:
            attachment_count += 1
            mt = a.get("mimeType") or a.get("mime_type")
            if mt:
                attachment_mime_types[mt] += 1
            src = a.get("source")
            if src:
                attachment_sources[src] += 1
            if len(attachment_examples) < 20:
                attachment_examples.append(a)

    # Citations
    cits = metadata.get("citations")
    if isinstance(cits, list):
        for c in cits:
            citation_count += 1
            cft = c.get("citation_format_type")
            if cft:
                citation_format_types[cft] += 1

    # Content references
    crs = metadata.get("content_references")
    if isinstance(crs, list):
        for r in crs:
            content_ref_count += 1
            t = r.get("type")
            if t:
                content_ref_types[t] += 1

    # Aggregate result
    ar = metadata.get("aggregate_result")
    if isinstance(ar, dict):
        aggregate_result_count += 1

    # Canvas
    canv = metadata.get("canvas")
    if isinstance(canv, dict):
        canvas_count += 1
        tdt = canv.get("textdoc_type")
        if tdt:
            canvas_types[tdt] += 1

    # Search
    srgs = metadata.get("search_result_groups")
    if isinstance(srgs, list) and len(srgs) > 0:
        search_result_group_count += 1

    # Dictation
    if metadata.get("dictation") is True:
        dictation_count += 1

    # Async tasks
    ati = metadata.get("async_task_id")
    if ati:
        async_task_count += 1
        att = metadata.get("async_task_type")
        if att:
            async_task_types[att] += 1


def analyse_message(message: dict):
    global total_messages

    total_messages += 1

    author = message.get("author", {})
    if isinstance(author, dict):
        author_roles[safe_str(author.get("role"))] += 1
        name = author.get("name")
        if name:
            author_names[name] += 1

    content = message.get("content")
    if isinstance(content, dict):
        analyse_content(content)

    recip = message.get("recipient")
    recipient_values[safe_str(recip)] += 1

    chan = message.get("channel")
    if chan:
        channel_values[chan] += 1

    stat = message.get("status")
    if stat:
        status_values[stat] += 1

    w = message.get("weight")
    weight_values[safe_str(w)] += 1

    # Tool dispatch cross-tab
    if recip and recip != "all":
        aname = (author or {}).get("name", "(no name)")
        tool_dispatch[f"{safe_str(recip)} ← {safe_str(aname)}"] += 1

    meta = message.get("metadata")
    if isinstance(meta, dict):
        analyse_message_metadata(meta)


def analyse_conversation(conv: dict):
    global total_conversations, total_nodes, null_message_nodes

    total_conversations += 1
    track_keys(conv, conv_field_presence)

    dms = conv.get("default_model_slug")
    if dms:
        default_model_slugs[dms] += 1

    gt = conv.get("gizmo_type")
    gizmo_types[safe_str(gt)] += 1

    gid = conv.get("gizmo_id")
    if gid:
        gizmo_ids[gid] += 1

    ms = conv.get("memory_scope")
    memory_scope_values[safe_str(ms)] += 1

    tid = conv.get("conversation_template_id")
    if tid:
        template_ids[tid] += 1

    mapping = conv.get("mapping", {})
    if isinstance(mapping, dict):
        for node_id, node in mapping.items():
            total_nodes += 1
            msg = node.get("message") if isinstance(node, dict) else None
            if isinstance(msg, dict):
                analyse_message(msg)
            else:
                null_message_nodes += 1


# ══════════════════════════════════════════════════════════════════════════════
# Streaming file processor
# ══════════════════════════════════════════════════════════════════════════════

def process_file(filepath: str):
    """Stream a JSON array of conversations using ijson."""
    size_mb = os.path.getsize(filepath) / 1024 / 1024
    fname = os.path.basename(filepath)
    print(f"Processing: {fname} ({size_mb:.1f} MB)...")

    with open(filepath, "rb") as f:
        # ijson.items streams top-level array elements one at a time
        for conv in ijson.items(f, "item"):
            analyse_conversation(conv)

    print(f"  → {total_conversations} conversations so far")


# ══════════════════════════════════════════════════════════════════════════════
# Output formatting
# ══════════════════════════════════════════════════════════════════════════════

def fmt_counter(title: str, counter: Counter, top: int = 50) -> str:
    lines = [f"\n### {title}\n"]
    if not counter:
        lines.append("(none)\n")
        return "\n".join(lines)
    lines.append("| Value | Count |")
    lines.append("|-------|------:|")
    for val, cnt in counter.most_common(top):
        lines.append(f"| `{val}` | {cnt} |")
    return "\n".join(lines)


def generate_report(files_processed: list[str]) -> str:
    r = []
    r.append("# ChatGPT Export Analysis Report\n")
    r.append(f"**Files analysed:** {', '.join(files_processed)}\n")
    r.append(f"| Metric | Value |")
    r.append(f"|--------|------:|")
    r.append(f"| Total conversations | {total_conversations} |")
    r.append(f"| Total tree nodes | {total_nodes} |")
    r.append(f"| Null-message nodes (roots) | {null_message_nodes} |")
    r.append(f"| Total messages | {total_messages} |")
    r.append(f"| Image asset pointers | {image_asset_pointers} |")
    r.append(f"| File attachments | {attachment_count} |")
    r.append(f"| Citations | {citation_count} |")
    r.append(f"| Content references | {content_ref_count} |")
    r.append(f"| Code executions (aggregate_result) | {aggregate_result_count} |")
    r.append(f"| Canvas documents | {canvas_count} |")
    r.append(f"| Reasoning recaps | {reasoning_recap_count} |")
    r.append(f"| Thoughts blocks | {thoughts_count} |")
    r.append(f"| Messages with search results | {search_result_group_count} |")
    r.append(f"| Dictated (voice) messages | {dictation_count} |")
    r.append(f"| Async tasks (deep research) | {async_task_count} |")
    r.append(f"| Computer output (Operator) | {computer_output_count} |")
    r.append(f"| User editable context (custom instructions) | {user_editable_context_count} |")
    r.append(f"| Citable code output | {citable_code_output_count} |")

    r.append(fmt_counter("Content Types", content_types))
    r.append(fmt_counter("Author Roles", author_roles))
    r.append(fmt_counter("Author Names (non-null)", author_names))
    r.append(fmt_counter("Recipient Values", recipient_values))
    r.append(fmt_counter("Channel Values", channel_values))
    r.append(fmt_counter("Message Status", status_values))
    r.append(fmt_counter("Weight Values", weight_values))
    r.append(fmt_counter("Model Slugs (message metadata)", model_slugs))
    r.append(fmt_counter("Default Model Slugs (conversation-level)", default_model_slugs))
    r.append(fmt_counter("Gizmo Types", gizmo_types))
    r.append(fmt_counter("Memory Scope", memory_scope_values))
    r.append(fmt_counter("Image Asset Pointer Schemes", image_asset_schemes))
    r.append(fmt_counter("Image Asset Metadata Keys", image_asset_metadata_keys))
    r.append(fmt_counter("Multimodal Part Types", multimodal_part_types))
    r.append(fmt_counter("Attachment MIME Types", attachment_mime_types, 40))
    r.append(fmt_counter("Attachment Sources", attachment_sources))
    r.append(fmt_counter("Citation Format Types", citation_format_types))
    r.append(fmt_counter("Content Reference Types", content_ref_types))
    r.append(fmt_counter("Canvas Document Types", canvas_types))
    r.append(fmt_counter("Async Task Types", async_task_types))
    r.append(fmt_counter("System Error Names", system_error_names))
    r.append(fmt_counter("Tether Quote URL Schemes", tether_url_schemes))
    r.append(fmt_counter("Tool Dispatch (recipient ← author.name)", tool_dispatch))
    r.append(fmt_counter("Conversation Template IDs", template_ids))

    r.append(fmt_counter("Conversation-Level Field Presence", conv_field_presence))
    r.append(fmt_counter("Message Metadata Fields (non-null)", msg_metadata_fields))
    r.append(fmt_counter("Content-Level Fields (non-null)", content_fields))

    # Content type examples
    r.append("\n### Content Type Examples (first occurrence, truncated)\n")
    for ct in sorted(content_type_examples):
        r.append(f"#### `{ct}`\n")
        r.append(f"```json\n{content_type_examples[ct]}\n```\n")

    # Attachment examples
    if attachment_examples:
        r.append("\n### Attachment Examples (first 20)\n")
        r.append("```json")
        for a in attachment_examples:
            r.append(json.dumps(a, ensure_ascii=False, indent=2))
        r.append("```\n")

    return "\n".join(r)


# ══════════════════════════════════════════════════════════════════════════════
# Main
# ══════════════════════════════════════════════════════════════════════════════

def main():
    files = sorted(
        glob.glob(str(REPO_ROOT / "conversations*.json")),
        key=lambda f: os.path.getsize(f),
    )
    if not files:
        print("No conversations*.json files found in repo root.")
        sys.exit(1)

    print(f"Found {len(files)} export file(s)\n")
    fnames = []
    for f in files:
        fnames.append(os.path.basename(f))
        process_file(f)

    print("\nGenerating report...")
    report = generate_report(fnames)

    out_path = REPO_ROOT / "docs" / "export-analysis-raw.md"
    out_path.write_text(report, encoding="utf-8")
    print(f"Report written to {out_path}")

    # Also print summary to console
    print(f"\n{'='*60}")
    print(f"SUMMARY")
    print(f"{'='*60}")
    print(f"Conversations: {total_conversations}")
    print(f"Messages:      {total_messages}")
    print(f"Content types: {len(content_types)}")
    print(f"Author roles:  {dict(author_roles)}")
    print(f"Top content types: {content_types.most_common(15)}")


if __name__ == "__main__":
    main()
