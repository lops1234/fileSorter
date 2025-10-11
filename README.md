# File Tagger

A Windows .NET 8 WPF application for tagging files and organizing them with metadata.

## Features

- **System Tray Application**: Runs in the background and stays in the taskbar
- **Distributed SQLite Database**: Main database + per-directory databases for scalable storage
- **Windows Explorer Integration**: Right-click context menus for managing tags
- **🏷️ Tag from Temp Files**: Add/manage tags directly from temporary search result folders
- **Directory Watching**: Configure which directories to enable tagging for
- **Cross-Directory Tag Management**: Use tags from any watched directory
- **Intelligent Tag Synchronization**: Automatic sync between directory and main databases
- **Smart Tag Search**: Autocomplete search with existing tags only
- **Manual Search Control**: Search files only when requested, not automatically loaded
- **File Filtering**: Filter files by tags both in the app and in Explorer
- **Advanced Search Syntax**: Boolean logic with AND, OR, NOT, and grouping operators
- **Temporary Results Caching**: Smart caching avoids re-copying unchanged search results

## Getting Started

### Prerequisites

- Windows 10 or later
- .NET 8 Runtime
- Minimum screen resolution: 1200x900 for optimal viewing (windows are pre-sized for comfortable use)

### Installation

1. Build the application:
   ```
   dotnet build --configuration Release
   ```

2. Run the application:
   ```
   dotnet run
   ```

### First-Time Setup

1. **Configure Watched Directories**: 
   - Open the application from the system tray
   - Go to the "Settings" tab
   - Click "Add Directory" to select folders where you want to enable tagging

2. **Create Tags** (Optional - you can also create tags while tagging files):
   - Go to the "Tag Management" tab
   - Enter a tag name and optional description
   - Click "Create Tag" to create standalone tags (available for use immediately)
   - Standalone tags will have 0 usage count until you attach them to files

### Using the Application

#### Adding Tags to Files

**Creating Tags:**

There are multiple ways to create tags:

1. **Standalone Tags (No Files Required)**:
   - Main Application → "Tag Management" tab → Enter name and description → "Create Tag"
   - Creates tags in all watched directories for immediate use

2. **While Tagging Files**:
   - In tag management window: "Create & Add" (creates and attaches to current file)
   - In tag management window: "Create Only" (creates without attaching to current file)

**Method 1: Through Windows Explorer**
- Right-click on any file in a watched directory
- Select "Manage Tags" from the context menu
- Add new tags or select from existing ones

**Method 2: Through the Application**
- Open the File Tagger application
- Go to the "File Browser" tab
- Right-click on a file and select "Manage Tags"

#### Viewing File Tags

- Right-click on any file in a watched directory
- Select "View File Tags" to see all tags assigned to that file

#### Advanced Tag Search

File Tagger supports powerful search syntax for finding files with complex tag combinations:

**Search Operators:**
- **Spaces or `&`** = AND: `photo work` or `photo & work`
- **`|`** = OR: `image | video`
- **`()`** = Grouping: `(urgent | important) & project`
- **`-`** = Exclude: `-archived document`

**Search Examples:**
- `photo work` - Files with BOTH "photo" AND "work" tags
- `image | video` - Files with EITHER "image" OR "video" tags
- `(urgent | important) & project` - Files with ("urgent" OR "important") AND "project"
- `document -archived` - Files with "document" tag but NOT "archived" tag
- `(photo | image) & work -temp` - Files with ("photo" OR "image") AND "work" but NOT "temp"

#### Filtering Files by Tags

**Search Options:**
- **Search Button**: Execute tag-based search OR find untagged files (when search box is empty)
- **Search All Button**: Find all files in watched directories (both tagged and untagged)
- **Clear Button**: Reset search and clear results

**In the Application:**
- Go to the "File Browser" tab
- Type advanced search query in the search box (autocomplete will show available tags)
- Click "Search" to filter files matching the query (or find untagged files if search is empty)
- Click "Search All" to see every file in your watched directories
- Click "Open in Explorer" to copy all search result files to a temporary folder and open it in Explorer
  - **Smart Caching**: If search parameters haven't changed, opens existing results without re-copying files
  - **Auto-Cancellation**: New searches automatically cancel any ongoing file copying operations
  - **Tag from Temp Files**: Right-click any file in the temp folder to add/manage tags - they apply to the original files!
  - **Works with Untagged Files**: Add tags to previously untagged files directly from temp directories
- File copying operations can be cancelled by performing new searches (Search, Search All, Clear)
- Right-click on any file to:
  - Open the file
  - Open folder containing the file in Explorer
  - Manage file tags
  - Copy file path to clipboard
- Click "?" for syntax help
- Use "Clear" to reset the search
- Use "Refresh Tags" to update the tag list after adding new tags

**In Windows Explorer:**
- Right-click on any directory
- Select "Filter Files by Tags"
- Type advanced search query in the search box (autocomplete will show available tags)
- Click "Search" to filter files matching the query
- Click "Open in Explorer" to copy all filtered files to a temporary folder and open it in Explorer
- Right-click on any file to:
  - Open the file
  - Open folder containing the file in Explorer
  - Manage file tags
  - Copy file path to clipboard
- Click "?" for syntax help

### Temporary Results Feature

When you click "Open in Explorer", File Tagger:
1. **Immediately opens Explorer** with a README.txt file containing your search parameters
2. **Starts copying files in the background** - you can watch them appear in real-time
3. **Shows progress** in the status bar (e.g., "Copying files... (5/20)")
4. **Handles duplicate filenames** automatically (adds _1, _2, etc.)
5. **Reports final status** with any errors encountered
6. **Cleans up previous results** when opening new ones
7. **Automatically deletes** the temporary directory when File Tagger closes

**Benefits of this approach:**
- **Instant feedback**: See your search parameters immediately
- **Real-time progress**: Watch files appear as they're copied
- **Non-blocking**: Continue using the application while copying
- **Error reporting**: Know immediately if any files couldn't be copied

**Note**: These are copies of your files - the originals remain in their original locations untouched.

#### 🏷️ Tag Management from Temporary Files

**Revolutionary Feature**: You can add and manage tags directly from temporary search result folders!

**How It Works:**
1. **Open search results** in Explorer using "Open in Explorer" button
2. **Right-click any file** in the temporary folder
3. **Select "Manage Tags"** from the context menu
4. **Add/remove tags** using the tag management window
5. **Tags are applied to the original files** in their actual locations

**Smart File Mapping:**
- File Tagger maintains a mapping between temporary copies and original files
- Works with both **tagged** and **untagged** files
- Handles **duplicate filenames** automatically (file_1.txt → file.txt)
- Uses **three-tier fallback system** for robust file mapping:
  1. **Direct mapping** from recent copy operations
  2. **Search cache** from recent search parameters  
  3. **Database lookup** of all files in watched directories

**Visual Feedback:**
When managing tags for a temp file, the tag management window displays:
- **⚠️ Temporary File Detected** information panel
- **Temp file path** (grayed out)
- **Original file path** (highlighted in blue)
- **Clear confirmation** that changes apply to the original file

**Use Cases:**
- **Quick tagging** after search operations
- **Bulk tagging** of search results
- **First-time tagging** of previously untagged files
- **Organizing files** without navigating through directory structures

**Performance Features:**
- **Smart caching**: Re-opening same search results skips file copying
- **Instant Explorer access**: Open multiple Explorer windows for same results
- **Automatic cleanup**: Temp files deleted when application closes
- **Cancellation support**: New searches cancel ongoing copy operations



### System Tray Features

The application minimizes to the system tray when closed, showing a blue folder icon with "T" for "Tags". The icon is created programmatically for optimal display quality. Right-click the tray icon to access:
- Open Settings
- Manage Tags
- Exit

**Note**: The tray icon appears whenever File Tagger is running and disappears when the application is closed.

## Troubleshooting

### Context Menu Behavior

**Normal Operation:**
- Context menus appear when File Tagger is running
- Context menus disappear when File Tagger is completely closed
- Context menus should NOT disappear while File Tagger is running

**If Context Menus Disappear While App is Running:**

1. **Use the Manual Refresh**: 
   - Open File Tagger application
   - Go to Settings tab
   - Click "Refresh Context Menus" button

2. **Restart Windows Explorer**:
   - Press `Ctrl+Shift+Esc` to open Task Manager
   - Find "Windows Explorer" and click "Restart"

3. **Re-add Directory**:
   - Remove the directory from watched list
   - Add it back again (this triggers context menu refresh)

**Note:** Context menus are automatically removed when you close File Tagger and re-added when you start it. This is the intended behavior to keep your system clean.

### Tags Show Count But Search Returns No Results

**Symptom**: Tags in the Tag Management tab show a usage count (e.g., "5 files"), but when you search for that tag, no files are found.

**Cause**: This happens when files are deleted or moved outside of File Tagger. The database still has records of these files, which count toward tag usage, but they don't exist anymore.

**Solution** - Try these steps in order:

**Step 1: Quick Fix (Try this first)**
1. Go to **Tag Management** tab
2. Click **"Refresh Tags"** button
3. This automatically removes tags with zero actual file associations
4. Try searching again

**Step 2: Deep Clean (If step 1 doesn't fix it)**
1. Go to **Settings** tab
2. Click **"Verify Tagged Files"** button
3. This will:
   - Check all tagged files to ensure they exist
   - Remove database records for missing files
   - Update tag counts automatically
4. Go back to Tag Management and verify counts are correct
5. Try searching again - the counts will now match actual results

**Why Both Steps?**
- **Refresh Tags**: Removes tags that have no file associations at all (quick)
- **Verify Tagged Files**: Removes file records for files that no longer exist on disk (thorough)

**Recommendation**: 
- Run "Refresh Tags" anytime counts look suspicious
- Run "Verify Tagged Files" periodically if you frequently move or delete files outside of File Tagger

### Database Architecture

**Main Database**: `%APPDATA%\FileTagger\main.db`
- Stores watched directories and aggregated tag information
- Tracks tag usage across all directories

**Directory Databases**: `[WatchedDirectory]\.filetagger\tags.db`
- One database per watched directory
- Stores local file records and tags for that directory
- Files are stored by relative path within the directory

## Technical Details

- **Framework**: .NET 8 with WPF
- **Database**: Distributed SQLite with Entity Framework Core
  - Main database for directory management and tag aggregation
  - Per-directory databases for local file tagging
  - Automatic synchronization between databases
- **Shell Integration**: Windows Registry context menus
- **UI Library**: WPF with custom styling and Hardcodet.NotifyIcon for system tray
- **Architecture**: Service-based design with DatabaseManager singleton

## Architecture

- `Data/`: Entity Framework models and database contexts
  - `MainModels.cs`: Models for main database (directories, aggregated tags)
  - `DirectoryModels.cs`: Models for directory databases (files, local tags)
  - `MainDbContext.cs`: Main database context
  - `DirectoryDbContext.cs`: Directory-specific database context
- `Services/`: Business logic and database management
  - `DatabaseManager.cs`: Manages distributed database operations and synchronization
- `Windows/`: WPF windows and dialogs
- `ShellIntegration.cs`: Windows Explorer context menu integration
- `CommandLineHandler.cs`: Handles context menu command line arguments
- `MainWindow.xaml/cs`: Main application interface

### Hierarchical Directory System

**Directory Inclusion:**
- When you add a directory to settings, ALL subdirectories are automatically included for tagging
- You can tag files anywhere within the directory tree

**Tag Storage Logic:**
- Tags created in subdirectories are stored in the parent directory's database (the one added in settings)
- If you have overlapping directories in settings (e.g., `C:\Documents` and `C:\Documents\Photos`):
  - Files in `C:\Documents\Photos` will have tags stored in BOTH databases
  - This allows for both broad categorization and specific organization
  - Search results automatically consolidate tags from all applicable databases

**Database Creation:**
- New databases are only created for directories explicitly added to settings
- Subdirectories use their parent's database
- Each database is stored in a hidden `.filetagger` folder within the directory

## License

This project is open source and available under the MIT License.