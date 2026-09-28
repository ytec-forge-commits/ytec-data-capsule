# SignPath Foundation application draft

This document is a reviewable draft for the SignPath Foundation application.
Do not add personal contact details, account tokens, or compatibility-key bytes
to this file.

## Project

- Name: Y-TEC Data Capsule
- Repository: https://github.com/ytec-forge-commits/ytec-data-capsule
- Product page: https://ytec.cloudfree.jp/forge/en/projects/data-capsule/
- Japanese product page: https://ytec.cloudfree.jp/forge/projects/data-capsule/
- Platform: Windows 7 SP1 through Windows 11, x86 and x64
- Distribution: portable ZIP containing a .NET Framework 4.6.1 WPF executable
- License: Apache License 2.0 for all Y-TEC-owned code, documentation, icon
  artwork, screenshots, build scripts, and other repository components

## Purpose and behavior

Y-TEC Data Capsule creates a new, non-overwriting backup folder for ordinary
Windows user data, supported Japanese postcard and accounting-data candidates,
Thunderbird data, Apple MobileSync data, browser bookmarks, and Wi-Fi profiles.
It also provides separate restore screens for Wi-Fi profiles and Chrome, Edge,
and Firefox bookmarks. Browser passwords, cookies, history, and sessions are
excluded. The app performs no telemetry, network communication, cloud sync,
automatic update, or remote API access.

The executable requests elevation because a completed NTFS/ReFS backup receives
owner Everyone and inheritable full control, limited to the newly created output
from that run. exFAT is supported without ACL processing and FAT32 is rejected.
Source data and source ACLs are never changed.

## Build and release provenance

- GitHub Actions builds on GitHub-hosted `windows-2022` runners.
- CI verifies formatting, localization, the public-source boundary, AnyCPU, x86,
  x64, and 46 synthetic regression tests.
- Actions are pinned to full commit SHAs.
- A version tag must match `Directory.Build.props`.
- The SignPath request consumes the GitHub artifact ID produced in the same run.
- The `signing` GitHub Environment provides the manual approval gate.
- The signed output is checked for product metadata, version, and an Authenticode
  signature before it can be assembled into a release.

## Code signing policy and roles

- Policy: https://github.com/ytec-forge-commits/ytec-data-capsule/blob/main/CODE_SIGNING_POLICY.md
- Privacy policy: https://github.com/ytec-forge-commits/ytec-data-capsule/blob/main/PRIVACY.md
- Committers and reviewers: Y-TEC organization owners
- Signing approvers: Y-TEC organization owners
- GitHub and SignPath accounts used by the project are protected with MFA.
- Every signing request requires manual approval.

## Required disclosure: Wi-Fi compatibility input

The public source builds and tests the complete application using a documented
public development Wi-Fi key. Official Y-TEC distributions inject one non-public
32-byte compatibility key from a protected GitHub Actions secret. The input is
configuration data rather than executable source code, and its only purpose is
to let an official build decrypt Wi-Fi containers made by another official build
without asking the user for a password. It is not a signing key and is not claimed
to provide strong confidentiality against executable analysis.

The temporary key file is created under `RUNNER_TEMP`, is never printed or
uploaded, and is removed in an unconditional cleanup step. Public development
builds are intentionally incompatible and clearly warn against real Wi-Fi data.

Please confirm whether this disclosed non-public compatibility input is acceptable
under the Foundation's fully open-source and verifiable-build requirements. Y-TEC
will follow the Foundation's decision and will not attempt to conceal or bypass it.
