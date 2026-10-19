# Confirmed native library notice additions

These are version-specific additions, not a complete binary SBOM. Candidate code-section/vendor matching is recorded in NATIVE-COVERAGE.md. Binary version strings and the pinned lockfile identify the first ten crates; zstd-sys is the locked native dependency of the detected zstd-safe crate. Only root license texts and the bundled Zstandard BSD text are retained. No samples, development tools or unrelated Cargo.lock entries are asserted to ship. The authoritative crate archives were SHA-256 checked against Velopack Cargo.lock before reading their license files; no crate code was executed.

| Crate/version | Declared license alternatives | Archive checksum | Source |
| --- | --- | --- | --- |
| gif 0.14.2 | MIT OR Apache-2.0 | `ee8cfcc411d9adbbaba82fb72661cc1bcca13e8bba98b364e62b2dba8f960159` | [gif](https://static.crates.io/crates/gif/gif-0.14.2.crate) |
| image 0.25.10 | MIT OR Apache-2.0 | `85ab80394333c02fe689eaf900ab500fbd0c2213da414687ebf995a65d5a6104` | [image](https://static.crates.io/crates/image/image-0.25.10.crate) |
| png 0.18.1 | MIT OR Apache-2.0 | `60769b8b31b2a9f263dae2776c37b1b28ae246943cf719eb6946a1db05128a61` | [png](https://static.crates.io/crates/png/png-0.18.1.crate) |
| rustls 0.23.45 | Apache-2.0 OR ISC OR MIT | `0d41d731c7d2f962d1ccc364cec258de3c0e93b38c2fb3ba97ac74513048d634` | [rustls](https://static.crates.io/crates/rustls/rustls-0.23.45.crate) |
| rustls-pki-types 1.15.1 | MIT OR Apache-2.0 | `2f4925028c7eb5d1fcdaf196971378ed9d2c1c4efc7dc5d011256f76c99c0a96` | [rustls-pki-types](https://static.crates.io/crates/rustls-pki-types/rustls-pki-types-1.15.1.crate) |
| rustls-webpki 0.103.15 | ISC | `f3c3cf1d8b1e7d4927e2d154c3fcb02979afb9939629c62cd9048d4f07b60ac2` | [rustls-webpki](https://static.crates.io/crates/rustls-webpki/rustls-webpki-0.103.15.crate) |
| ureq 3.4.2 | MIT OR Apache-2.0 | `9a7ac20be9b7726e0bbdbf974c059676d9acb1cd414961f570a4e8231cacd7fc` | [ureq](https://static.crates.io/crates/ureq/ureq-3.4.2.crate) |
| ureq-proto 0.6.4 | MIT OR Apache-2.0 | `f86fd172ccca569e458f61b6bdd6220965a9ef36e672a6852953b51a0e1583be` | [ureq-proto](https://static.crates.io/crates/ureq-proto/ureq-proto-0.6.4.crate) |
| zip 8.6.0 | MIT | `2d04a6b5381502aa6087c94c669499eb1602eb9c5e8198e534de571f7154809b` | [zip](https://static.crates.io/crates/zip/zip-8.6.0.crate) |
| zstd-safe 8.0.0 | BSD-3-Clause | `ae42c0555055784c70058d19ba8e275528e8a99a706684868ace5da4e716a4ab` | [zstd-safe](https://static.crates.io/crates/zstd-safe/zstd-safe-8.0.0.crate) |
| zstd-sys 2.1.0+zstd.1.5.7 | BSD-3-Clause | `0ef0a8027ec3ee71300ab3bcbcd0393f434aa72b91ca6d635a39941deae8eea0` | [zstd-sys](https://static.crates.io/crates/zstd-sys/zstd-sys-2.1.0+zstd.1.5.7.crate) |

## Verbatim file provenance

All texts preserve upstream wording/copyright. Multiple offered licenses are retained without licensing the application under those terms. Zstandard uses the supplied BSD-3-Clause texts; the packaging-only zstd.exe tool is not the shipped native library.

| File | SHA-256 | Archive member |
| --- | --- | --- |
| [Native-gif-0.14.2-LICENSE-APACHE.txt](Native-gif-0.14.2-LICENSE-APACHE.txt) | `769f80b5bcb42ed0af4e4d2fd74e1ac9bf843cb80c5a29219d1ef3544428a6bb` | gif-0.14.2/LICENSE-APACHE |
| [Native-gif-0.14.2-LICENSE-MIT.txt](Native-gif-0.14.2-LICENSE-MIT.txt) | `77257f3d2181236b1aee78920238062ae64efe13c5d858b2db126e79c9e1b14f` | gif-0.14.2/LICENSE-MIT |
| [Native-image-0.25.10-LICENSE-APACHE.txt](Native-image-0.25.10-LICENSE-APACHE.txt) | `0d542e0c8804e39aa7f37eb00da5a762149dc682d7829451287e11b938e94594` | image-0.25.10/LICENSE-APACHE |
| [Native-image-0.25.10-LICENSE-MIT.txt](Native-image-0.25.10-LICENSE-MIT.txt) | `c77a4cf9da729987d0fe7ccd811e3bd27393914ddf3d23467c18cc22954513b3` | image-0.25.10/LICENSE-MIT |
| [Native-png-0.18.1-LICENSE-APACHE.txt](Native-png-0.18.1-LICENSE-APACHE.txt) | `a60eea817514531668d7e00765731449fe14d059d3249e0bc93b36de45f759f2` | png-0.18.1/LICENSE-APACHE |
| [Native-png-0.18.1-LICENSE-MIT.txt](Native-png-0.18.1-LICENSE-MIT.txt) | `eaf40297c75da471f7cda1f3458e8d91b4b2ec866e609527a13acfa93b638652` | png-0.18.1/LICENSE-MIT |
| [Native-rustls-0.23.45-LICENSE-APACHE.txt](Native-rustls-0.23.45-LICENSE-APACHE.txt) | `a60eea817514531668d7e00765731449fe14d059d3249e0bc93b36de45f759f2` | rustls-0.23.45/LICENSE-APACHE |
| [Native-rustls-0.23.45-LICENSE-ISC.txt](Native-rustls-0.23.45-LICENSE-ISC.txt) | `7cfafc877eccc46c0e346ccbaa5c51bb6b894d2b818e617d970211e232785ad4` | rustls-0.23.45/LICENSE-ISC |
| [Native-rustls-0.23.45-LICENSE-MIT.txt](Native-rustls-0.23.45-LICENSE-MIT.txt) | `709e3175b4212f7b13aa93971c9f62ff8c69ec45ad8c6532a7e0c41d7a7d6f8c` | rustls-0.23.45/LICENSE-MIT |
| [Native-rustls-pki-types-1.15.1-LICENSE-APACHE.txt](Native-rustls-pki-types-1.15.1-LICENSE-APACHE.txt) | `45fd05c4865e7c350b98ad7ac50e1b15462d49af4a91e9b0c9dd933dc9a69742` | rustls-pki-types-1.15.1/LICENSE-APACHE |
| [Native-rustls-pki-types-1.15.1-LICENSE-MIT.txt](Native-rustls-pki-types-1.15.1-LICENSE-MIT.txt) | `9117d922e667125508dde62b02c1f57ed22f5ad21eb536aa2e2d99e1c796e639` | rustls-pki-types-1.15.1/LICENSE-MIT |
| [Native-rustls-webpki-0.103.15-LICENSE.txt](Native-rustls-webpki-0.103.15-LICENSE.txt) | `5b698ca13897be3afdb7174256fa1574f8c6892b8bea1a66dd6469d3fe27885a` | rustls-webpki-0.103.15/LICENSE |
| [Native-ureq-3.4.2-LICENSE-APACHE.txt](Native-ureq-3.4.2-LICENSE-APACHE.txt) | `c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4` | ureq-3.4.2/LICENSE-APACHE |
| [Native-ureq-3.4.2-LICENSE-MIT.txt](Native-ureq-3.4.2-LICENSE-MIT.txt) | `7e5886959f67f8c75063a9d55cd3cb08c5a8cbea9a944fa1df2d4ae038f53721` | ureq-3.4.2/LICENSE-MIT |
| [Native-ureq-proto-0.6.4-LICENSE-APACHE.txt](Native-ureq-proto-0.6.4-LICENSE-APACHE.txt) | `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30` | ureq-proto-0.6.4/LICENSE-APACHE.txt |
| [Native-ureq-proto-0.6.4-LICENSE-MIT.txt](Native-ureq-proto-0.6.4-LICENSE-MIT.txt) | `5b460f37be48ac4cf2181596181cc6cae432fc2caae05944873f380730829a4b` | ureq-proto-0.6.4/LICENSE-MIT.txt |
| [Native-zip-8.6.0-LICENSE.txt](Native-zip-8.6.0-LICENSE.txt) | `58545fed1565e42d687aecec6897d35c6d37ccb71479a137c0deb2203e125c79` | zip-8.6.0/LICENSE |
| [Native-zstd-safe-8.0.0-LICENSE.txt](Native-zstd-safe-8.0.0-LICENSE.txt) | `ce4868c8e4ebe47a7d4b3c93dbf6b802033e5ae2fa7638098173f80836daac60` | zstd-safe-8.0.0/LICENSE |
| [Native-zstd-sys-2.1.0+zstd.1.5.7-LICENSE.txt](Native-zstd-sys-2.1.0+zstd.1.5.7-LICENSE.txt) | `ce4868c8e4ebe47a7d4b3c93dbf6b802033e5ae2fa7638098173f80836daac60` | zstd-sys-2.1.0+zstd.1.5.7/LICENSE |
| [Native-zstd-sys-2.1.0+zstd.1.5.7-LICENSE.BSD-3-Clause.txt](Native-zstd-sys-2.1.0+zstd.1.5.7-LICENSE.BSD-3-Clause.txt) | `bf7017a73a95ea63bf3716b13ec2bcd79c92942356e3408c6fb66332bad6d977` | zstd-sys-2.1.0+zstd.1.5.7/LICENSE.BSD-3-Clause |
| [Native-zstd-sys-2.1.0+zstd.1.5.7-zstd-LICENSE.txt](Native-zstd-sys-2.1.0+zstd.1.5.7-zstd-LICENSE.txt) | `7055266497633c9025b777c78eb7235af13922117480ed5c674677adc381c9d8` | zstd-sys-2.1.0+zstd.1.5.7/zstd/LICENSE |
