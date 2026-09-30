# Eikura v1.3.5

- Added a WebDAV configuration backup and restore card under Settings → General.
- Portable backups cover media sources, in-app TMDB configuration, metadata settings, playback/subtitle settings, and appearance.
- The entire backup is encrypted with a key derived using PBKDF2-SHA256 and protected with AES-256-GCM.
- Connection information for the selected backup destination itself is excluded to avoid a circular restore dependency.
- Local folders that do not exist on another device are skipped and reported during restore.
- Playback history, the media database, cache, window geometry, and local-file access state are outside the v1.3.5 backup scope.
- Bumped product / MSIX versions to 1.3.5 / 1.3.5.0.
