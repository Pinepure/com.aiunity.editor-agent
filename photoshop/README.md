# Photoshop Platform

This folder contains the Adobe Photoshop adapter for the AI Platform Agent Framework.

The implementation uses:

- a local Python companion service for the shared HTTP protocol
- a UXP plugin that runs inside Photoshop and executes official Photoshop DOM operations
- a bridge folder contract instead of brittle GUI automation
