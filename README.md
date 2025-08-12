# File Tagger

A Windows .NET 8 WPF application for tagging files and organizing them with metadata.

## Features

- **System Tray Application**: Runs in the background and stays in the taskbar
- **SQLite Database**: Stores file tags and metadata locally
- **Windows Explorer Integration**: Right-click context menus for managing tags
- **Directory Watching**: Configure which directories to enable tagging for
- **Tag Management**: Create, view, and organize tags
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
- Use the tag filter dropdown to show only files with specific tags

**In Windows Explorer:**
- Right-click on any directory
- Select "Filter Files by Tags"
- Choose a tag to filter by and browse the results

### System Tray Features

The application minimizes to the system tray when closed. Right-click the tray icon to access:
- Open Settings
- Manage Tags
- Exit

### Database Location

The SQLite database is stored at:
`%APPDATA%\FileTagger\filetagger.db`

## Technical Details

- **Framework**: .NET 8 with WPF
- **Database**: SQLite with Entity Framework Core
- **Shell Integration**: Windows Registry context menus
- **UI Library**: WPF with custom styling and Hardcodet.NotifyIcon for system tray

## Architecture

- `Data/`: Entity Framework models and database context
- `Windows/`: WPF windows and dialogs
- `ShellIntegration.cs`: Windows Explorer context menu integration
- `CommandLineHandler.cs`: Handles context menu command line arguments
- `MainWindow.xaml/cs`: Main application interface

## License

This project is open source and available under the MIT License.