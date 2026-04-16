# Migrating AD Accounts from RC4 to AES Kerberos Encryption

## What's Happening

Microsoft is deprecating RC4 encryption for Kerberos. Starting with the **April 2026** cumulative update, domain controllers will enter enforcement mode and stop accepting RC4 by default. The **July 2026** update makes this permanent with no rollback option.

Any account where `msDS-SupportedEncryptionTypes` is **empty or 0** currently falls back to RC4 — these accounts will be impacted.

## msDS-SupportedEncryptionTypes Quick Reference

The attribute is a bitmask (add the values together):

| Bit Value | Encryption Type |
| --------- | --------------- |
| 4         | RC4             |
| 8         | AES128          |
| 16        | AES256          |

| Value  | What It Means                        |
| ------ | ------------------------------------ |
| 0/null | Defaults to RC4 **(fix these)**      |
| 24     | AES128 + AES256 **(target state)**   |
| 28     | RC4 + AES128 + AES256 (transitional) |

Use **28** if you need a safety net while testing. Use **24** when you're confident everything supports AES.

## How to Migrate an Account

```powershell
# 1. Check current value
Get-ADUser -Identity "AccountName" -Properties "msDS-SupportedEncryptionTypes" |
    Select-Object Name, "msDS-SupportedEncryptionTypes"

# 2. Set to AES-only (or 28 for transitional)
Set-ADUser -Identity "AccountName" -Replace @{"msDS-SupportedEncryptionTypes" = 24}

# For computer accounts use Set-ADComputer instead

# 3. Reset the password — AES keys won't be generated without this
Set-ADAccountPassword -Identity "AccountName" -Reset -NewPassword (Read-Host -AsSecureString "New Password")

# 4. Verify
Get-ADUser -Identity "AccountName" -Properties "msDS-SupportedEncryptionTypes" |
    Select-Object Name, "msDS-SupportedEncryptionTypes"
```

> **The password reset is critical.** Without it the account won't have AES keys in AD regardless of what value you set.

## Finding Accounts That Need Attention

```powershell
# Find user accounts with SPNs that have no encryption type set
Get-ADUser -Filter {ServicePrincipalName -like "*"} -Properties "msDS-SupportedEncryptionTypes", "ServicePrincipalName" |
    Where-Object { -not $_."msDS-SupportedEncryptionTypes" } |
    Select-Object Name, "msDS-SupportedEncryptionTypes"
```

## Monitoring After Changes

On domain controllers, watch for:

- **Event ID 4769** — look for encryption type `0x17` (RC4). You want to see `0x12` (AES256).
- **Event ID 201/202** (System log) — new RC4 usage warnings added in the January 2026 updates.

## If Something Breaks

Set the value to **28** to re-enable RC4 as a fallback while you investigate:

```powershell
Set-ADUser -Identity "AccountName" -Replace @{"msDS-SupportedEncryptionTypes" = 28}
```

## References

- [Decrypting the Selection of Supported Kerberos Encryption Types](https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/decrypting-the-selection-of-supported-kerberos-encryption-types/1628797) — bitmask values and Kerberos encryption explained
- [AD Hardening Part 4 — Enforcing AES for Kerberos](https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/active-directory-hardening-series-part-4-enforcing-aes-for-kerberos/4114965) — migration sequencing guidance
- [Detect and Remediate RC4 Usage in Kerberos](https://learn.microsoft.com/en-us/windows-server/security/kerberos/detect-remediate-rc4-kerberos) — official detection scripts
- [What Is Going On with RC4 in Kerberos?](https://techcommunity.microsoft.com/blog/askds/what-is-going-on-with-rc4-in-kerberos/4489365) — April/July 2026 enforcement timeline
- [Beyond RC4 for Windows Authentication](https://www.microsoft.com/en-us/windows-server/blog/2025/12/03/beyond-rc4-for-windows-authentication/) — Microsoft's security baseline values and detection tooling
