# shaun

A CLI for downloading media from Bluesky and Twitter/X posts.

## Usage
```
Usage:
  shaun <urls>... [options]

Arguments:
  <urls>  URLs to download from

Options:
  -a, --also <also>      Additional authors (comma-delimited Bluesky handles/DIDs or Twitter
                         handles; bang-delimited per-attachment)
  -c, --compact          Omit DIDs from Bluesky filenames
  -p, --parallel         Download media in parallel
  -m, --mtime            Set each file's modification time to the post's reported publishing time
  -q, --quotes           Follow quote posts recursively and download their media too
  -o, --output <output>  Output directory (defaults to the current directory)
```

## Configuration (Twitter-only)
```
[shaun]
filename_pattern={user-id}_{date-time}_{status-id}_{file-type}
auth_token=
ct0=
```

`auth_token` and `ct0` are both optional, but must be populated for restricted media.
