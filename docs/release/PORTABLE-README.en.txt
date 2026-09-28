Y-TEC Data Capsule 1.2.0
Official portable release (2026-09-29 republication; Y-TEC self-signed)

START

1. Extract the ZIP to a folder.
2. Start Y-TEC Data Capsule.exe.
3. When Windows User Account Control appears, verify the file source and allow it.

Keep the EXE together with its DLLs, config folder, Operation Manual folder,
and User Manual folder. The app requires no installation and performs no
telemetry, network communication, or cloud synchronization.

SYSTEM REQUIREMENTS

- Windows 7 SP1, 8, 8.1, 10, or 11
- 32-bit or 64-bit Windows; the AnyCPU executable selects the runtime automatically
- .NET Framework 4.6.1 or later

IMPORTANT SAFETY INFORMATION

- Source data is never deleted, moved, renamed, or overwritten.
- Each run creates a new backup folder. An existing file or folder is never reused.
- On NTFS and ReFS, only the new output from the current run receives owner
  Everyone and inheritable full control. exFAT is supported but cannot store
  Windows ACLs, so ACL processing is skipped. FAT32 is rejected before copying.
- Internet Explorer, Edge, Chrome, and Firefox backups contain bookmarks only.
  Passwords, cookies, history, and sessions are excluded.
- The target browser is closed before bookmark backup or restore. Save unfinished
  browser form input before continuing.
- Wi-Fi profiles are saved in an encrypted transfer file. The embedded-key model
  prevents casual plaintext exposure but has limited resistance to executable analysis.
- OneDrive, iCloud, and similar online-only placeholders are skipped without being
  downloaded. Files already available locally continue to be copied.
- When available, VSS is used as a fallback for files that are open. Remaining
  failures are reported in the completion result and backup manifest.

AFTER BACKUP

The User Manual folder contains HTML and PDF instructions for:

- manually moving ordinary files to a new Windows profile;
- importing or moving Thunderbird data and re-entering excluded passwords;
- restoring iPhone or iPad MobileSync data with Apple Devices or iTunes;
- opening supported Japanese postcard-software address books;
- treating accounting candidates only as a supplement to the vendor's official backup;
- restoring Wi-Fi profiles and Chrome, Edge, or Firefox bookmarks.

SIGNATURE, HASHES, AND RIGHTS

Y-TEC's EXE and two DLLs are self-signed; the third-party DLL retains its original
signature. This is not a Microsoft- or commercial-CA-trusted certificate.
SmartScreen warnings may appear. The public CER is optional verification material;
the app never installs certificates. Compare the ZIP SHA-256 with the value
published by Y-TEC and the final release checksum list. SHA256SUMS.txt covers each
file after extraction.

Official page: https://ytec.cloudfree.jp/forge/en/projects/data-capsule/
Contact: https://ytec.cloudfree.jp/forge/contact/
There is no published Store edition. Update manually into a separate folder and
keep existing backups. Official Wi-Fi interoperability and saved formats are unchanged.

Y-TEC-owned code, documentation, icon artwork, and screenshots are licensed
under Apache License 2.0. Modification, commercial use, and redistribution are
permitted under that license. See LICENSE.txt, NOTICE, BRAND_POLICY.md,
ASSET_PROVENANCE.md, and THIRD-PARTY-NOTICES.txt. Modified builds must not
imply Y-TEC approval or official status. Product names, company names, and
trademarks belong to their respective owners.
