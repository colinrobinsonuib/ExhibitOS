# Video Test Artworks

Place test video files (`.mp4`, `.mkv`, `.mov`, `.avi`, `.webm`, `.m4v`) in this folder.

ExhibitOS will:
- Discover all video files in this folder.
- Play them alphabetically in continuous fullscreen loop using bundled `mpv`.
- Hide the mouse cursor automatically.
- Route audio to the exhibition configured sound output device.

*(Video media files are ignored by git in `.gitignore` to prevent bloating the repository.)*
