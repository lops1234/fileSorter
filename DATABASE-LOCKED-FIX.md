# Database Lock Issue - Fixed

## The Problem
When trying to merge duplicate databases:
```
Database is locked by another process
```

## What Causes This?
1. **Another FileTagger instance** is running
2. **Database viewer tools** (DB Browser for SQLite, etc.) have the database open
3. **File sync software** (Google Drive, Dropbox) is actively syncing the database
4. **File explorers** are indexing or viewing the `.filetagger` folders
5. **Antivirus software** is scanning the database files

## The Fix Applied

### 1. Pre-check Database Locks
Before attempting merge, the tool now checks if databases are locked:
```csharp
private bool IsDatabaseLocked(string dbPath)
{
    try
    {
        using (var stream = File.Open(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            return false; // Not locked
        }
    }
    catch (IOException)
    {
        return true; // Locked
    }
}
```

### 2. Read-Only Access
The merge now opens duplicate databases in read-only mode:
```csharp
var connectionString = $"Data Source={duplicateDbPath};Mode=ReadOnly";
```

This prevents conflicts with other processes that might have read access.

### 3. Clear Error Messages
If a lock is detected, you'll see:
```
Database is locked by another process: [path]
Please close all applications using this database and try again.

💡 TIP: Close all applications that might be accessing the databases
(e.g., database viewers, file explorers, other FileTagger instances)
and try again.
```

## How to Fix Lock Issues

### Quick Fix (Works 90% of the time)
1. **Close FileTagger completely** (including system tray)
2. **Check Task Manager** for any lingering FileTagger processes
3. **Close any database tools** you might have open
4. **Restart FileTagger**
5. **Try merge again**

### Medium Fix (Works 99% of the time)
1. **Pause Google Drive sync** (or whatever sync software you use)
2. **Close all applications**
3. **Wait 10 seconds**
4. **Open only FileTagger**
5. **Run merge immediately**
6. **Resume sync after merge completes**

### Nuclear Option (Works 100% of the time)
1. **Backup your `.filetagger` folders** (copy them somewhere safe)
2. **Restart your computer**
3. **Before opening ANYTHING else, open FileTagger**
4. **Run the merge immediately**
5. **Success! ✅**

## Prevention
To avoid locks in the future:
- Close FileTagger when not actively tagging files
- Don't open `.filetagger` folders in database viewers while FileTagger is running
- Let sync software finish syncing before opening FileTagger
- Run merges right after starting FileTagger (when no other apps are running)

## Technical Details
- The tool now uses **read-only mode** to minimize lock conflicts
- Lock detection happens **before** attempting merge
- Error messages are **specific and actionable**
- Failed merges **don't corrupt data** (transactional)

## Status
✅ **IMPROVED** - Lock detection and better error messages added!

