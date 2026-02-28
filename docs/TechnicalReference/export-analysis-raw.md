# ChatGPT Export Analysis Report

**Files analysed:** conversations-006.json, conversations-010.json, conversations-007.json, conversations-012.json, conversations-009.json, conversations-008.json, conversations-011.json, conversations.json

| Metric | Value |
|--------|------:|
| Total conversations | 2913 |
| Total tree nodes | 82823 |
| Null-message nodes (roots) | 2913 |
| Total messages | 79910 |
| Image asset pointers | 1378 |
| File attachments | 7999 |
| Citations | 8651 |
| Content references | 13106 |
| Code executions (aggregate_result) | 773 |
| Canvas documents | 854 |
| Reasoning recaps | 788 |
| Thoughts blocks | 1042 |
| Messages with search results | 798 |
| Dictated (voice) messages | 3 |
| Async tasks (deep research) | 27 |
| Computer output (Operator) | 15 |
| User editable context (custom instructions) | 1170 |
| Citable code output | 13 |

### Content Types

| Value | Count |
|-------|------:|
| `text` | 66123 |
| `tether_quote` | 5161 |
| `code` | 1931 |
| `multimodal_text` | 1569 |
| `tether_browsing_display` | 1244 |
| `user_editable_context` | 1170 |
| `thoughts` | 1042 |
| `reasoning_recap` | 788 |
| `execution_output` | 773 |
| `system_error` | 81 |
| `computer_output` | 15 |
| `citable_code_output` | 13 |

### Author Roles

| Value | Count |
|-------|------:|
| `assistant` | 33432 |
| `user` | 25724 |
| `tool` | 11719 |
| `system` | 9035 |

### Author Names (non-null)

| Value | Count |
|-------|------:|
| `file_search` | 5909 |
| `myfiles_browser` | 1840 |
| `python` | 807 |
| `bio` | 601 |
| `web.run` | 548 |
| `t2uay3k.sj1i4kz` | 423 |
| `dalle.text2im` | 328 |
| `canmore.update_textdoc` | 325 |
| `browser` | 302 |
| `canmore.create_textdoc` | 288 |
| `web` | 215 |
| `a8km123` | 17 |
| `diagrams_show_me.get__MermaidRoute` | 16 |
| `canmore.comment_textdoc` | 13 |
| `container.exec` | 13 |
| `computer.do` | 11 |
| `api_tool.search_tools` | 8 |
| `whimsical_com__jit_plugin.postRenderFlowchart` | 6 |
| `diagrams_helpful_dev__jit_plugin.get_DiagramSyntaxDocumentation` | 6 |
| `api_tool.list_resources` | 6 |
| `diagrams_show_me.get_DiagramGuidelinesRoute` | 6 |
| `n7jupd.metadata` | 5 |
| `computer.sync_file` | 4 |
| `computer.initialize` | 3 |
| `q7dr546` | 3 |
| `sironix_app__jit_plugin.generateResumePdfLink` | 3 |
| `diagrams_helpful_dev__jit_plugin.post_RenderDiagram` | 2 |
| `research_kickoff_tool.start_research_task` | 2 |
| `research_kickoff_tool.clarify_with_text` | 2 |
| `api_tool.call_tool` | 1 |
| `computer.get` | 1 |
| `dalle.text` | 1 |
| `diagrams_show_me.get_ShowIdeasRoute` | 1 |
| `MixerBox_ImageGen_Al_image_generation.imageGeneration` | 1 |
| `csv_creator.create_csv` | 1 |
| `csv_creator.create_csv_v2` | 1 |

### Recipient Values

| Value | Count |
|-------|------:|
| `all` | 75441 |
| `assistant` | 1020 |
| `python` | 824 |
| `bio` | 602 |
| `canmore.update_textdoc` | 327 |
| `web` | 316 |
| `canmore.create_textdoc` | 288 |
| `web.run` | 262 |
| `t2uay3k.sj1i4kz` | 224 |
| `browser` | 175 |
| `dalle.text2im` | 168 |
| `web.search` | 72 |
| `file_search.msearch` | 54 |
| `diagrams_show_me.get__MermaidRoute` | 17 |
| `file_search.mclick` | 16 |
| `canmore.comment_textdoc` | 13 |
| `container.exec` | 13 |
| `myfiles_browser` | 11 |
| `computer.do` | 11 |
| `web.open_url` | 10 |
| `api_tool.search_tools` | 8 |
| `api_tool.list_resources` | 6 |
| `diagrams_show_me.get_DiagramGuidelinesRoute` | 6 |
| `computer.sync_file` | 4 |
| `computer.initialize` | 3 |
| `q7dr546` | 3 |
| `whimsical_com__jit_plugin.postRenderFlowchart` | 2 |
| `diagrams_helpful_dev__jit_plugin.get_DiagramSyntaxDocumentation` | 2 |
| `diagrams_helpful_dev__jit_plugin.post_RenderDiagram` | 2 |
| `research_kickoff_tool.start_research_task` | 2 |
| `api_tool.call_tool` | 1 |
| `computer.get` | 1 |
| `dalle.text` | 1 |
| `sironix_app__jit_plugin.generateResumePdfLink` | 1 |
| `diagrams_show_me.get_ShowIdeasRoute` | 1 |
| `MixerBox_ImageGen_Al_image_generation.imageGeneration` | 1 |
| `csv_creator.create_csv` | 1 |
| `csv_creator.create_csv_v2` | 1 |

### Channel Values

| Value | Count |
|-------|------:|
| `final` | 2479 |
| `commentary` | 233 |

### Message Status

| Value | Count |
|-------|------:|
| `finished_successfully` | 79672 |
| `in_progress` | 237 |
| `finished_partial_completion` | 1 |

### Weight Values

| Value | Count |
|-------|------:|
| `1.0` | 72026 |
| `0.0` | 7884 |

### Model Slugs (message metadata)

| Value | Count |
|-------|------:|
| `gpt-4o` | 27465 |
| `gpt-5` | 7743 |
| `gpt-4` | 4634 |
| `gpt-5-thinking` | 2907 |
| `gpt-5-2` | 2887 |
| `gpt-5-1` | 1287 |
| `text-davinci-002-render-sha` | 1238 |
| `gpt-4-plugins` | 407 |
| `gpt-5-1-thinking` | 92 |
| `gpt-5-2-thinking` | 86 |
| `gpt-4o-canmore` | 58 |
| `gpt-4-5` | 54 |
| `o3-mini-high` | 44 |
| `gpt-4-gizmo` | 25 |
| `text-davinci-002-render` | 11 |
| `research` | 10 |
| `gpt-4l` | 7 |
| `agent-mode` | 5 |
| `gpt-5-auto-thinking` | 3 |

### Default Model Slugs (conversation-level)

| Value | Count |
|-------|------:|
| `gpt-4o` | 1374 |
| `gpt-5` | 355 |
| `gpt-4` | 143 |
| `gpt-5-2` | 119 |
| `gpt-5-1` | 66 |
| `gpt-5-thinking` | 18 |
| `auto` | 6 |
| `gpt-4o-canmore` | 6 |
| `text-davinci-002-render-sha` | 5 |
| `gpt-4-5` | 4 |
| `research` | 2 |

### Gizmo Types

| Value | Count |
|-------|------:|
| `(null)` | 2155 |
| `snorlax` | 748 |
| `gpt` | 10 |

### Memory Scope

| Value | Count |
|-------|------:|
| `global_enabled` | 2759 |
| `project_enabled` | 154 |

### Image Asset Pointer Schemes

| Value | Count |
|-------|------:|
| `file-service` | 1052 |
| `sediment` | 326 |

### Image Asset Metadata Keys

| Value | Count |
|-------|------:|
| `sanitized` | 1348 |
| `dalle` | 385 |
| `container_pixel_height` | 224 |
| `container_pixel_width` | 224 |
| `generation` | 224 |

### Multimodal Part Types

| Value | Count |
|-------|------:|
| `string` | 67767 |
| `image_asset_pointer` | 1378 |

### Attachment MIME Types

| Value | Count |
|-------|------:|
| `text/markdown` | 2174 |
| `text/x-csharp` | 1686 |
| `image/png` | 1420 |
| `text/plain` | 751 |
| `text/javascript` | 270 |
| `text/css` | 254 |
| `text/tsx` | 176 |
| `image/jpeg` | 149 |
| `video/mp2t` | 140 |
| `application/pdf` | 98 |
| `text/csv` | 45 |
| `text/html` | 27 |
| `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | 26 |
| `text/yaml` | 23 |
| `image/svg+xml` | 21 |
| `application/xaml+xml` | 20 |
| `application/json` | 18 |
| `application/zip` | 16 |
| `application/fusion` | 12 |
| `text/vtt` | 8 |
| `application/x-yaml` | 8 |
| `image/heic` | 8 |
| `application/msword` | 6 |
| `image/gif` | 6 |
| `text/xml` | 4 |
| `audio/mpeg` | 2 |
| `model/stl` | 2 |
| `application/octet-stream` | 2 |
| `application/rtf` | 2 |
| `application/vnd.openxmlformats-officedocument.presentationml.presentation` | 2 |
| `image/tiff` | 1 |
| `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | 1 |

### Attachment Sources

| Value | Count |
|-------|------:|
| `local` | 281 |

### Citation Format Types

| Value | Count |
|-------|------:|
| `tether_og` | 448 |
| `berry_file_search` | 144 |
| `tether_v4` | 102 |

### Content Reference Types

| Value | Count |
|-------|------:|
| `hidden` | 6416 |
| `attribution` | 2406 |
| `grouped_webpages` | 2388 |
| `sources_footnote` | 583 |
| `file` | 441 |
| `webpage_extended` | 251 |
| `grouped_webpages_model_predicted_fallback` | 240 |
| `entity` | 115 |
| `image_v2` | 71 |
| `product_entity` | 47 |
| `alt_text` | 43 |
| `webpage` | 32 |
| `image_group` | 20 |
| `video` | 19 |
| `navigation` | 12 |
| `products` | 10 |
| `nav_list` | 6 |
| `businesses_map` | 4 |
| `tldr` | 2 |

### Canvas Document Types

| Value | Count |
|-------|------:|
| `document` | 623 |
| `code/csharp` | 46 |
| `code/xml` | 42 |
| `code/python` | 22 |
| `code/html` | 16 |
| `code/c` | 10 |
| `code/other` | 8 |
| `code/json` | 6 |
| `code/javascript` | 6 |
| `code/cpp` | 6 |
| `code/bash` | 2 |

### Async Task Types

| Value | Count |
|-------|------:|
| `image_gen` | 23 |
| `research` | 2 |

### System Error Names

| Value | Count |
|-------|------:|
| `GetDownloadLinkError` | 20 |
| `tool_error` | 19 |
| `InvalidFilePointerError` | 13 |
| `MissingSourceFilter` | 8 |
| `TransportError` | 6 |
| `ClientResponseError` | 4 |
| `ChatGPTAgentToolException` | 3 |
| `ConnectionClosedError` | 2 |
| `MessageParseError` | 2 |
| `InvalidField` | 1 |
| `UnrecognizedFunctionError` | 1 |
| `ConnectionResetError` | 1 |
| `AceConnectionException` | 1 |

### Tether Quote URL Schemes

| Value | Count |
|-------|------:|
| `file-id` | 4965 |
| `https` | 187 |
| `(other)` | 9 |

### Tool Dispatch (recipient ← author.name)

| Value | Count |
|-------|------:|
| `python ← (null)` | 824 |
| `bio ← (null)` | 602 |
| `assistant ← bio` | 601 |
| `canmore.update_textdoc ← (null)` | 327 |
| `web ← (null)` | 316 |
| `canmore.create_textdoc ← (null)` | 288 |
| `web.run ← (null)` | 262 |
| `t2uay3k.sj1i4kz ← (null)` | 224 |
| `assistant ← web` | 213 |
| `assistant ← t2uay3k.sj1i4kz` | 199 |
| `browser ← (null)` | 175 |
| `dalle.text2im ← (null)` | 168 |
| `web.search ← (null)` | 72 |
| `file_search.msearch ← (null)` | 54 |
| `diagrams_show_me.get__MermaidRoute ← (null)` | 17 |
| `file_search.mclick ← (null)` | 16 |
| `canmore.comment_textdoc ← (null)` | 13 |
| `container.exec ← (null)` | 13 |
| `myfiles_browser ← (null)` | 11 |
| `computer.do ← (null)` | 11 |
| `web.open_url ← (null)` | 10 |
| `api_tool.search_tools ← (null)` | 8 |
| `api_tool.list_resources ← (null)` | 6 |
| `diagrams_show_me.get_DiagramGuidelinesRoute ← (null)` | 6 |
| `assistant ← whimsical_com__jit_plugin.postRenderFlowchart` | 4 |
| `computer.sync_file ← (null)` | 4 |
| `computer.initialize ← (null)` | 3 |
| `q7dr546 ← (null)` | 3 |
| `whimsical_com__jit_plugin.postRenderFlowchart ← (null)` | 2 |
| `diagrams_helpful_dev__jit_plugin.get_DiagramSyntaxDocumentation ← (null)` | 2 |
| `assistant ← diagrams_helpful_dev__jit_plugin.get_DiagramSyntaxDocumentation` | 2 |
| `diagrams_helpful_dev__jit_plugin.post_RenderDiagram ← (null)` | 2 |
| `research_kickoff_tool.start_research_task ← (null)` | 2 |
| `api_tool.call_tool ← (null)` | 1 |
| `computer.get ← (null)` | 1 |
| `dalle.text ← (null)` | 1 |
| `sironix_app__jit_plugin.generateResumePdfLink ← (null)` | 1 |
| `assistant ← sironix_app__jit_plugin.generateResumePdfLink` | 1 |
| `diagrams_show_me.get_ShowIdeasRoute ← (null)` | 1 |
| `MixerBox_ImageGen_Al_image_generation.imageGeneration ← (null)` | 1 |
| `csv_creator.create_csv ← (null)` | 1 |
| `csv_creator.create_csv_v2 ← (null)` | 1 |

### Conversation Template IDs

| Value | Count |
|-------|------:|
| `g-p-67992a14e0d0819192f808fa99118e46` | 156 |
| `g-p-67ad7b9b1cd481919caf6c343aaf3f7e` | 156 |
| `g-p-67b314204e8c8191bf44bd8345d048c5` | 105 |
| `g-p-67e9bf7e21f081919868948c29f1b0a6` | 66 |
| `g-p-67f19e5f66908191be41657b2cf48d4f` | 41 |
| `g-p-6877fce1d9908191a22495c92fe7737b` | 29 |
| `g-p-68c4d92e9c108191b8ce6f056976b93b` | 19 |
| `g-p-683a73732cbc8191b9164b160c16e2a8` | 18 |
| `g-p-68bd0cf5be888191bc8ff120fb662ba7` | 17 |
| `g-p-675cab2724d08191b13fc88b4e713dc1` | 16 |
| `g-p-6937a496b444819193251360ff52a6b9` | 15 |
| `g-p-68b22e467a408191844933f81414d19b` | 14 |
| `g-p-68d12ed557a4819183a7b29ca627ca22` | 12 |
| `g-p-6883de4b26288191b302d0f71d5ddb50` | 12 |
| `g-p-6838030a736c8191a0d097975c79ae69` | 12 |
| `g-p-67ecd737a2348191ab21d42bca414634` | 11 |
| `g-p-68ca38f96db481919ae63bbfa9727ccc` | 10 |
| `g-p-68acfaf3ca788191b7c3fd7378a87202` | 10 |
| `g-p-68e8a5773a4481919c8f4e37be5a7d6e` | 7 |
| `g-p-6866b86e38408191a3739f01243013aa` | 6 |
| `g-p-67e1ba2e60208191821e87d6b2c66803` | 4 |
| `g-p-68a2d9e60a9c8191b4dfa893d19576ed` | 4 |
| `g-p-6901b4a79a2c8191b0478dac2ba9d1e0` | 4 |
| `g-vI2kaiM9N` | 2 |
| `g-5QhhdsfDj` | 2 |
| `g-p-67fb0288d058819182296c1160690bb2` | 2 |
| `g-p-680e95e7f1c08191a545834202a896e2` | 2 |
| `g-lGwWBnHUN` | 1 |
| `g-dn3XaYeNS` | 1 |
| `g-8kF3JPCme` | 1 |
| `g-F3NWdiPBp` | 1 |
| `g-hQdMJ5mJh` | 1 |
| `g-cTqsEOE4C` | 1 |

### Conversation-Level Field Presence

| Value | Count |
|-------|------:|
| `blocked_urls` | 2913 |
| `conversation_id` | 2913 |
| `create_time` | 2913 |
| `current_node` | 2913 |
| `disabled_tool_ids` | 2913 |
| `id` | 2913 |
| `is_archived` | 2913 |
| `is_study_mode` | 2913 |
| `mapping` | 2913 |
| `memory_scope` | 2913 |
| `moderation_results` | 2913 |
| `safe_urls` | 2913 |
| `sugar_item_visible` | 2913 |
| `update_time` | 2913 |
| `title` | 2902 |
| `default_model_slug` | 2098 |
| `is_do_not_remember` | 1026 |
| `conversation_template_id` | 758 |
| `gizmo_id` | 758 |
| `gizmo_type` | 758 |
| `plugin_ids` | 65 |
| `async_status` | 25 |
| `is_starred` | 4 |
| `pinned_time` | 2 |

### Message Metadata Fields (non-null)

| Value | Count |
|-------|------:|
| `can_save` | 79910 |
| `timestamp_` | 68736 |
| `request_id` | 64044 |
| `model_slug` | 48963 |
| `parent_id` | 46546 |
| `default_model_slug` | 43542 |
| `finish_details` | 26136 |
| `is_complete` | 25438 |
| `citations` | 24588 |
| `content_references` | 22551 |
| `message_type` | 19830 |
| `serialization_metadata` | 17529 |
| `is_visually_hidden_from_conversation` | 17107 |
| `selected_github_repos` | 12157 |
| `turn_exchange_id` | 10812 |
| `selected_sources` | 10318 |
| `gizmo_id` | 9340 |
| `command` | 9285 |
| `dictation` | 5911 |
| `developer_mode_connector_ids` | 4124 |
| `reasoning_status` | 3409 |
| `pad` | 2406 |
| `classifier_response` | 2331 |
| `attachments` | 2210 |
| `is_contextual_answers_system_message` | 1870 |
| `contextual_answers_message_type` | 1840 |
| `status` | 1632 |
| `rebase_system_message` | 1606 |
| `is_user_system_message` | 1592 |
| `user_context_message_data` | 1525 |
| `rebase_developer_message` | 1223 |
| `search_result_groups` | 984 |
| `skip_reasoning_title` | 958 |
| `reasoning_title` | 918 |
| `canvas` | 854 |
| `finished_duration_sec` | 787 |
| `aggregate_result` | 773 |
| `retrieval_turn_number` | 704 |
| `client_reported_search_source` | 671 |
| `search_source` | 671 |
| `retrieval_file_index` | 655 |
| `category_suggestions` | 640 |
| `safe_urls` | 624 |
| `debug_sonic_thread_id` | 523 |
| `search_turns_count` | 458 |
| `args` | 298 |
| `_cite_metadata` | 296 |
| `kwargs` | 283 |
| `image_results` | 236 |
| `message_locale` | 213 |

### Content-Level Fields (non-null)

| Value | Count |
|-------|------:|
| `content_type` | 79910 |
| `parts` | 67692 |
| `text` | 7946 |
| `domain` | 5161 |
| `title` | 5161 |
| `url` | 5161 |
| `language` | 1931 |
| `result` | 1244 |
| `user_instructions` | 1170 |
| `user_profile` | 1170 |
| `summary` | 1098 |
| `source_analysis_msg_id` | 1042 |
| `thoughts` | 1042 |
| `content` | 788 |
| `assets` | 109 |
| `name` | 81 |
| `tether_id` | 16 |
| `computer_id` | 15 |
| `screenshot` | 15 |
| `state` | 15 |
| `output_str` | 13 |
| `metadata` | 13 |

### Content Type Examples (first occurrence, truncated)

#### `citable_code_output`

```json
{"content_type": "citable_code_output", "output_str": "{\"resources\":[{\"uri\":\"/GitHub/link_68ca1683395c8191bc3608fd6641cb4a/check_repo_initialized\",\"name\":\"GitHub_check_repo_initialized\",\"description\":\"Check if a GitHub repository has been set up.\",\"namespace_description\":\"Read repositories, issues, and pull requests.\",\"mime_type\":\"application/vnd.openai.pineapple-tool\",\"input_schema\":{\"title\":\"check_repo_initialized_input\",\"default\":{},\"type\":\"object\",\"properties\":{\"repo_id\":{\"title\":\"Repo Id\",\"default\":{},\"type\":\"integer\"}},\"required\":[\"repo_...
```

#### `code`

```json
{"content_type": "code", "language": "unknown", "response_format_name": null, "text": "search(\"I stopped following the LTT drama. I already had a low opinion of the channel - I watched a few videos, got excited about some promised content, and quickly learned that it was all pure entertainment rather than information, and consequently inaccurate and unreliable. However it's come up again in conversation recently so I did a little searching and now I can't piece the timeline together. From what I can tell, it looks like ultimately nothing changed, there was no final, heartfelt, actual apology,...
```

#### `computer_output`

```json
{"computer_id": "0", "content_type": "computer_output", "is_ephemeral": null, "screenshot": {"asset_pointer": "sediment://file_00000000aecc61f797b921bc9f7ee255", "content_type": "image_asset_pointer", "fovea": 768, "height": 768, "metadata": null, "size_bytes": 914644, "width": 1024}, "state": {"dom": null, "id": "4892a27e95104af4bd2f392141fe0efa", "title": "GitHub · Build and ship software on a single, collaborative platform · GitHub", "type": "browser_state", "url": "https://github.com/verdaniq/mailcaddy"}, "tether_id": 184498611074752}
```

#### `execution_output`

```json
{"content_type": "execution_output", "text": "---------------------------------------------------------------------------\nModuleNotFoundError                       Traceback (most recent call last)\nCell In[2], line 1\n----> 1 import webvtt\n      3 # Path to the uploaded VTT transcript file\n      4 vtt_path = \"/mnt/data/BDD Episode 36_ Changing paths-20250114_120257-Meeting Recording-en-AU.vtt\"\n\nModuleNotFoundError: No module named 'webvtt'\n"}
```

#### `multimodal_text`

```json
{"content_type": "multimodal_text", "parts": [{"asset_pointer": "file-service://file-XWZMtijEMLcNnEu79zWNP4", "content_type": "image_asset_pointer", "fovea": null, "height": 1600, "metadata": {"asset_pointer_link": null, "container_pixel_height": null, "container_pixel_width": null, "dalle": null, "emu_omit_glimpse_image": null, "emu_patches_override": null, "generation": null, "gizmo": null, "is_no_auth_placeholder": null, "lpe_delta_encoding_channel": null, "lpe_keep_patch_ijhw": null, "sanitized": true, "watermarked_asset_pointer": null}, "size_bytes": 3139766, "width": 1200}, "Is this lett...
```

#### `reasoning_recap`

```json
{"content": "Thought for a couple of seconds", "content_type": "reasoning_recap"}
```

#### `system_error`

```json
{"content_type": "system_error", "name": "GetDownloadLinkError", "text": "Encountered exception: <class 'file_service_client.client.GetDownloadLinkError'>."}
```

#### `tether_browsing_display`

```json
{"assets": null, "content_type": "tether_browsing_display", "result": "", "summary": "", "tether_id": null}
```

#### `tether_quote`

```json
{"content_type": "tether_quote", "domain": "35.md", "tether_id": null, "text": "---\r\nepisode: 35\r\ntitle: 'New Year, new tools, new goals'\r\ndate: 2025-01-08\r\nlink: 'https://bddepisodes.blob.core.windows.net/episodes/BDD Episode 35.mp3'\r\nimage: images/episodes/ep35.png\r\nduration: 57min\r\nguid: 3a8c5a9f-6900-46ff-938e-63cb7b29dfaa\r\n---\r\nIn this episode, we reflect on New Year celebrations and personal milestones, sharing contrasting experiences from family gatherings to brewing a successful batch of lager. We discuss the pitfalls of traditional New Year's resolutions, advocating ...
```

#### `text`

```json
{"content_type": "text", "parts": [""]}
```

#### `thoughts`

```json
{"content_type": "thoughts", "source_analysis_msg_id": "fa75aab6-94a3-447f-a331-3a3e2f44f495", "thoughts": [{"chunks": [], "content": "Alright, I think the best move here is to find the hotel’s contact number by searching online to get the most direct information. Once I find it, I can help guide on the next steps. It's often easier to call directly with any questions or booking updates. Let's look up the number and make sure it’s up-to-date! \n\nOkay, time to search!", "finished": true, "summary": "Finding hotel contact info"}]}
```

#### `user_editable_context`

```json
{"content_type": "user_editable_context", "user_instructions": "The user provided the additional info about how they would like you to respond:\n```I generally don't need a lot of background information in responses. If I mention something, unless I'm specifically asking for information about it, I don't need context in the reply. For example, if I ask \"how do I center a div in css?\" I don't need an explanation of what CSS is or what a DIV is, I just want the answer. An explanation of the answer is fine,  but I don't need background context I haven't asked for. While I appreciate ChatGPT try...
```


### Attachment Examples (first 20)

```json
{
  "height": 1600,
  "id": "file-XWZMtijEMLcNnEu79zWNP4",
  "mime_type": "image/png",
  "name": "image.png",
  "size": 3139766,
  "width": 1200
}
{
  "file_token_size": 329,
  "id": "file-54gkhpAnoohK34ZQ1te1XH",
  "mime_type": "text/markdown",
  "name": "35.md",
  "size": 1355
}
{
  "height": 1030,
  "id": "file-AZQjd8N92FSdKhM6x4QPk7",
  "mime_type": "image/png",
  "name": "image.png",
  "size": 2218512,
  "width": 1037
}
{
  "file_token_size": 12421,
  "id": "file-DHXFE8YTU9Ycz887dsV2Bj",
  "mime_type": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "name": "BDD Episode 36_ Changing paths-20250114_120257-Meeting Recording-en-AU.docx",
  "size": 29024
}
{
  "id": "file-87sBZ7LLqDqqn7m9NvWP8x",
  "mime_type": "text/vtt",
  "name": "BDD Episode 36_ Changing paths-20250114_120257-Meeting Recording-en-AU.vtt",
  "size": 117943
}
{
  "id": "file-LSGxB7JqMdKFwcJ3MQt6i3",
  "mime_type": "audio/mpeg",
  "name": "BDD Episode 36_ Changing path.mp3",
  "size": 32922560
}
{
  "id": "file-AkB8LByazEE2eMXayVK1iQ",
  "mime_type": "",
  "name": "MarkdownRenderer.razor",
  "size": 1331
}
{
  "id": "file-4HRxHgSt576FrmDaW9yspp",
  "mime_type": "",
  "name": "Home.razor",
  "size": 2995
}
{
  "file_token_size": 2966,
  "id": "file-EnTZPoeAyidpTCSfx6fg6w",
  "mime_type": "text/x-csharp",
  "name": "PrismCodeBlockRenderer.cs",
  "size": 14276
}
{
  "file_token_size": 2940,
  "id": "file-5qSZiZUzrL2vjbAH7Ux3ke",
  "mime_type": "text/x-csharp",
  "name": "PrismCodeBlockRenderer.cs",
  "size": 14211
}
{
  "height": 139,
  "id": "file-CJUfYFd5W6E2VjrqpA5iFw",
  "mime_type": "image/png",
  "name": "image.png",
  "size": 16345,
  "width": 430
}
{
  "file_token_size": 3008,
  "id": "file-RMhQ5BEKHcvcPZ3LwmmHfQ",
  "mime_type": "text/x-csharp",
  "name": "PrismCodeBlockRenderer.cs",
  "size": 14537
}
{
  "height": 507,
  "id": "file-YW355AnfThTi8bUnTFnPoL",
  "mime_type": "image/png",
  "name": "image.png",
  "size": 52672,
  "width": 1853
}
{
  "fileSizeTokens": 115,
  "id": "file-3EABXpfrWKM1Mt1yFbcT9h",
  "mimeType": "text/x-csharp",
  "name": "Course.cs"
}
{
  "fileSizeTokens": 136,
  "id": "file-Hic53oKXbDvTCSoscyxf6A",
  "mimeType": "text/x-csharp",
  "name": "Chapter.cs"
}
{
  "fileSizeTokens": 107,
  "id": "file-NaDUXMbGeve2iYQ1p7g8kT",
  "mimeType": "text/x-csharp",
  "name": "User.cs"
}
{
  "fileSizeTokens": 93,
  "id": "file-6fWA5BqG1MkvPVxm3pzK3b",
  "mimeType": "text/x-csharp",
  "name": "UserCourse.cs"
}
{
  "fileSizeTokens": 84,
  "id": "file-6aMnspM55yRryLJLhsDFuf",
  "mimeType": "text/x-csharp",
  "name": "Module.cs"
}
{
  "fileSizeTokens": 108,
  "id": "file-9sCRw6Z2B1NhwYfrmULXLz",
  "mimeType": "text/plain",
  "name": "EditCourse.cshtml.cs"
}
{
  "fileSizeTokens": 1844,
  "id": "file-RtVCrth2z8pKC3nwo4GHEs",
  "mimeType": "text/javascript",
  "name": "editFunctions.js"
}
```
