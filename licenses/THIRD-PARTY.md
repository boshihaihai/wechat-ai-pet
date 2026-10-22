# Third-party components

## DeepSeek Harness

Authorization protocol and provider behavior adapted from the local `research-harness` source, principally `deepseek-account-platform`, `llm-deepseek-account`, `llm-deepseek-api-key`, and `llm-pi-ai`. Copyright (c) 2026 DeepSeek; MIT license in DSH-MIT.txt. The installed application does not require an installed Harness and does not launch its agent or tools.

## pi-ai

The ChatGPT token sharing flow is adapted from `@earendil-works/pi-ai` version 1.1.0, `dist/auth/oauth/openai-chatgpt.js`. Author: Mario Zechner; upstream https://github.com/earendil-works/pi. MIT terms reproduced in pi-ai-MIT.txt. This adaptation adds per-attempt loopback ports, JWT verification, isolated encrypted storage and explicit request limits.

## Node.js

Node.js v24.14.1 x64 is included as a private runtime. Node-LICENSE.txt contains its upstream license and bundled dependency notices, retrieved from the matching upstream tag.

## DSH whale artwork

The existing local SVG assets are included solely in this personal prototype. The artwork license is separate from the plugin code license; see Whale-ASSETS-LICENSE.md. This package is not an assertion of third-party redistribution rights.
