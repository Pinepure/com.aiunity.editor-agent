"""
Figma does not need an in-host execution bridge for the first adapter.

All host operations run against the official Figma REST API from the local
service process in `figma_client.py`.
"""
