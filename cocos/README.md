# Cocos Creator Adapter Plan

This folder reserves the top-level monorepo slot for a future Cocos Creator adapter.

Planned direction:

- expose the shared discovery-first HTTP protocol
- keep one stable Cocos extension host inside the editor
- load generated JavaScript tools from an adapter-owned `generated_tools/` directory
- avoid rewriting or reinstalling whole extensions when AI adds one new tool

This platform is part of the next dynamic-tool batch after `Flutter`, `Unreal`, and `Godot`.
