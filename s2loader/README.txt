s2 loader - save slots, outfits and unlocks for the Skate 2 PC port
==================================================================

BUILD (once)
  Run build.bat. It compiles s2loader.exe with the C# compiler that ships with
  Windows (.NET Framework 4, present on Windows 10/11). Font, backgrounds and icon
  are embedded, so s2loader.exe is the only file you need afterwards.
  A folder s2loader_data (slots, outfits, backups, settings) appears next to it.

FIRST RUN - setup tab
  game exe     - the Skate 2 port .exe
  save folder  - the folder with SKATER.P ("auto find" searches for it)

TABS
  play     - what goes into the game: save slot + outfit + options
             (max cash, unlock all gear, DLC, grab progress after playing). Enter = play.
  saves    - slots = separate save copies. grab from game, import (or drop SKATER.P
             onto the window), duplicate, rename, export, delete.
  outfits  - wardrobe: save the skater look from the game or a slot, attach a photo,
             export/import .s2fit to share. Double-click = wear it now.
  tools    - edit the selected save: cash, name, unlock all gear (+DLC), save look,
             fix crc, undo last, backups folder.
  setup    - paths, backup count, checklist.
  about    - credits, licenses, contacts.

PLAY
  1. the current game SKATER.P is backed up;
  2. the selected slot (with outfit and options) is written to the game folder;
  3. the game starts, the loader minimizes;
  4. when the game exits, progress is copied back into the slot (if enabled).
  If the game exe is a launcher that quits right away, press "grab progress" after playing.

Keys: Enter play, 1-6 tabs, F5 refresh, Esc quit. Every write makes a backup.

contact: https://t.me/emopunk  /  https://discord.gg/VzGdZZ5H8
Fan-made, not affiliated with Electronic Arts. Skate 2 (c) EA.
