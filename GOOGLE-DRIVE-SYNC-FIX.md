# Fix Google Drive Sync Conflicts in FileTagger

## The Problem
Google Drive creates duplicate `.filetagger` folders when sync conflicts occur:
- `.filetagger`
- `.filetagger (1)`
- `.filetagger (2)`
- etc.

This splits your tags across multiple databases, making some tags invisible when searching.

## The Solution - ONE BUTTON!

### Step 1: Close FileTagger
Make sure FileTagger is completely closed (including system tray).

### Step 2: Restart FileTagger
The new version has the merge functionality built in.

### Step 3: Merge Duplicates
1. Open FileTagger
2. Go to **Settings** tab
3. Click **"Merge Duplicate Databases"** button
4. Click **"Yes"** to confirm

### What Happens?
The tool will:
1. **Scan** all your watched directories for duplicate `.filetagger` folders
2. **Find** all `.filetagger (1)`, `.filetagger (2)`, etc.
3. **Merge** all tags, files, and associations into the main `.filetagger` database
4. **Delete** the duplicate folders automatically
5. **Synchronize** the tag counts

### Results You'll See
The confirmation dialog will show:
- How many duplicate databases were found
- How many tags were merged
- How many files were merged
- How many associations were merged
- Any errors (if they occurred)

### After Merging
- All your tags are in one place
- Search will find all tagged files
- No more duplicate folders
- Tag counts are accurate

## When to Run This
- After Google Drive sync conflicts
- When you notice `.filetagger (1)` or similar folders
- If some tags show up but files don't appear in search
- After moving directories between synced locations

## Troubleshooting

### "Database is locked by another process"

If you get this error:

**Cause**: Another application is accessing one of the duplicate databases.

**Solution**:
1. **Close all applications** that might be using the database:
   - Other FileTagger instances
   - Database viewers (DB Browser for SQLite, etc.)
   - File explorers showing the `.filetagger` folders
   - Any sync software (Google Drive, Dropbox, etc.) - pause syncing temporarily
2. **Wait a few seconds** for locks to release
3. **Try the merge again**

**Still locked?**
1. Restart your computer (this releases all file locks)
2. Before starting any other applications, run FileTagger
3. Immediately run the merge

### Databases Won't Merge
If databases keep failing to merge:
1. Make a **backup copy** of all `.filetagger` folders
2. Close ALL applications
3. Restart computer
4. Open only FileTagger
5. Run merge immediately

## Safe?
**YES!** The merge process:
- Never deletes tags (only merges them)
- Never deletes file associations (only merges them)
- Only removes duplicate database folders AFTER successful merge
- Keeps the newest information when conflicts exist
- Can be run multiple times safely

## Backup First?
If you want to be extra safe:
1. Copy your entire `.filetagger` folders somewhere else
2. Run the merge
3. Test that everything works
4. Delete the backup

But the merge is designed to be safe and non-destructive!

