# File Tagger

A Windows .NET 8 WPF application for tagging files and organizing them with metadata.

## Features

- **System Tray Application**: Runs in the background and stays in the taskbar
- **Distributed SQLite Database**: Main database + per-directory databases for scalable storage
- **Windows Explorer Integration**: Right-click context menus for managing tags
- **Directory Watching**: Configure which directories to enable tagging for
- **Cross-Directory Tag Management**: Use tags from any watched directory
- **Intelligent Tag Synchronization**: Automatic sync between directory and main databases
- **Smart Tag Search**: Autocomplete search with existing tags only
- **Manual Search Control**: Search files only when requested, not automatically loaded
- **File Filtering**: Filter files by tags both in the app and in Explorer

## Getting Started

### Prerequisites

- Windows 10 or later
- .NET 8 Runtime

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

2. **Create Tags**:
   - Go to the "Tag Management" tab
   - Enter a tag name and optional description
   - Click "Create Tag"

### Using the Application

#### Adding Tags to Files

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

#### Filtering Files by Tags

**In the Application:**
- Go to the "File Browser" tab
- Type in the tag search box (with autocomplete suggestions)
- Click "Search" to filter files by the entered tag
- Use "Clear" to reset the search
- Use "Refresh Tags" to update the tag list after adding new tags

**In Windows Explorer:**
- Right-click on any directory
- Select "Filter Files by Tags"
- Type in the tag search box (with autocomplete suggestions)
- Click "Search" to filter files by the entered tag

### System Tray Features

The application minimizes to the system tray when closed. Right-click the tray icon to access:
- Open Settings
- Manage Tags
- Exit

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

## License

This project is open source and available under the MIT License.