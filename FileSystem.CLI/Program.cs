using FileSystem.Core;
using FileSystem.Core.Utils;

class Program
{
    private static IFileSystemAPI _fileSystem = null!;
    private static string _containerPath = "../container.bin"; // Move to parent directory

    static void Main()
    {
        Console.WriteLine("=== Custom File System CLI ===");
        Console.WriteLine("Type 'help' for available commands or 'exit' to quit.");

        _fileSystem = new FileSystemImplementation();

        Console.Write("Create new container or open existing? (n = new, o = open) [o]: ");

        var modeInput = Console.ReadLine();
        var mode = TextUtils.IsNullOrWhiteSpace(modeInput) ? "o" : TextUtils.ToLower(TextUtils.Trim(modeInput ?? ""));

        if (mode == "n" || mode == "new")
        {
            Console.Write($"Enter path for new container (default {_containerPath}): ");

            var path = Console.ReadLine();
            path = TextUtils.IsNullOrWhiteSpace(path) ? _containerPath : TextUtils.Trim(path ?? "");

            Console.Write($"Enter block size in bytes (default {Layout.DefaultBlockSize}): ");

            var bsInput = Console.ReadLine();
            int blockSize = Layout.DefaultBlockSize;

            if (!TextUtils.IsNullOrWhiteSpace(bsInput) && int.TryParse(TextUtils.Trim(bsInput ?? ""), out var bs)) blockSize = bs;

            Console.Write($"Enter total blocks (default {Layout.DefaultTotalBlocks}): ");

            var tbInput = Console.ReadLine();
            int totalBlocks = Layout.DefaultTotalBlocks;

            if (!TextUtils.IsNullOrWhiteSpace(tbInput) && int.TryParse(TextUtils.Trim(tbInput ?? ""), out var tb)) totalBlocks = tb;

            Console.WriteLine($"Creating container: {path} (blockSize={blockSize}, totalBlocks={totalBlocks})");

            _fileSystem.CreateContainer(path, blockSize, totalBlocks);
            _containerPath = path;

            Console.WriteLine("Container created successfully.");
        }
        else
        {
            Console.Write($"Enter path to existing container (default {_containerPath}): ");

            var path = Console.ReadLine();
            path = TextUtils.IsNullOrWhiteSpace(path) ? _containerPath : TextUtils.Trim(path ?? "");

            if (!File.Exists(path))
            {
                Console.WriteLine($"Container not found: {path}. Creating new container with defaults.");

                _fileSystem.CreateContainer(path, Layout.DefaultBlockSize, Layout.DefaultTotalBlocks);
                _containerPath = path;

                Console.WriteLine("Container created successfully.");
            }
            else
            {
                Console.WriteLine($"Opening existing container: {path}");

                _fileSystem.OpenContainer(path);
                _containerPath = path;

                Console.WriteLine("Container opened successfully.");
            }
        }

        while (true)
        {
            Console.Write($"{_fileSystem.GetCurrentPath()}> ");
            string? input = Console.ReadLine();

            if (TextUtils.IsNullOrWhiteSpace(input))
                continue;

            try
            {
                var cleaned = TextUtils.Trim(input ?? "");
                ProcessCommand(cleaned);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    static void ProcessCommand(string input)
    {
        string[] parts = SplitCommand(input);
        if (parts.Length == 0) return;

        string command = TextUtils.ToLower(parts[0]);

        switch (command)
        {
            case "help":
                ShowHelp();
                break;
            case "exit":
                Environment.Exit(0);
                break;
            case "ls":
                ListFiles();
                break;
            case "cpin":
                if (parts.Length >= 3)
                    CopyFileIn(parts[1], parts[2]);
                else
                    Console.WriteLine("Usage: cpin <source_path> <target_name>");
                break;
            case "cpout":
                if (parts.Length >= 3)
                    CopyFileOut(parts[1], parts[2]);
                else
                    Console.WriteLine("Usage: cpout <file_name> <target_path>");
                break;
            case "rm":
                if (parts.Length >= 2)
                    DeleteFile(parts[1]);
                else
                    Console.WriteLine("Usage: rm <file_name>");
                break;
            case "md":
                if (parts.Length >= 2)
                    CreateDirectory(parts[1]);
                else
                    Console.WriteLine("Usage: md <directory_name>");
                break;
            case "cd":
                if (parts.Length >= 2)
                    ChangeDirectory(parts[1]);
                else
                    Console.WriteLine("Usage: cd <directory_name>");
                break;
            case "rd":
                if (parts.Length >= 2)
                    RemoveDirectory(parts[1]);
                else
                    Console.WriteLine("Usage: rd <directory_name>");
                break;
            case "info":
                ShowContainerInfo();
                break;
            default:
                Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands.");
                break;
        }
    }

    static string[] SplitCommand(string input)
    {
        var parts = new FileSystem.Core.Utils.Collections.SimpleList<string>();
        if (TextUtils.IsNullOrEmpty(input)) return parts.ToArray();

        bool inQuotes = false;

        char[] buffer = new char[input.Length];
        int bidx = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (bidx > 0)
                {
                    parts.Add(new string(buffer, 0, bidx));
                    bidx = 0;
                }
            }
            else
            {
                buffer[bidx++] = c;
            }
        }

        if (bidx > 0)
        {
            parts.Add(new string(buffer, 0, bidx));
        }

        return parts.ToArray();
    }

    static void ShowHelp()
    {
        Console.WriteLine("\nAvailable commands:");
        Console.WriteLine("  help                     - Show this help message");
        Console.WriteLine("  ls                       - List files and directories");
        Console.WriteLine("  cpin <source> <target>   - Copy file into container");
        Console.WriteLine("  cpout <file> <target>    - Copy file out of container");
        Console.WriteLine("  rm <file>                - Remove file");
        Console.WriteLine("  md <name>                - Create directory");
        Console.WriteLine("  cd <name>                - Change directory");
        Console.WriteLine("  rd <name>                - Remove directory");
        Console.WriteLine("  info                     - Show container information");
        Console.WriteLine("  exit                     - Exit the program");
        Console.WriteLine();
    }

    static void ListFiles()
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }

        var files = _fileSystem.ListCurrentDirectory();

        if (files.Count == 0)
        {
            Console.WriteLine("Directory is empty.");
            return;
        }

        Console.WriteLine("\nName\t\tSize\t\tType");
        Console.WriteLine("----------------------------------------");

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            string type = file.IsDirectory ? "<DIR>" : $"{file.Size}B";
            Console.WriteLine($"{file.Name,-15}\t{type,-10}\t{(file.IsDirectory ? "Directory" : "File")}");
        }
        Console.WriteLine();
    }

    static void CopyFileIn(string sourcePath, string targetName)
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }
        if (TextUtils.IsNullOrWhiteSpace(sourcePath) || TextUtils.IsNullOrWhiteSpace(targetName))
        {
            Console.WriteLine("Source path and target name must not be empty.");
            return;
        }
        if (!File.Exists(sourcePath))
        {
            Console.WriteLine($"Source file '{sourcePath}' does not exist.");
            return;
        }

        try
        {
            Console.WriteLine($"Copying '{sourcePath}' to container as '{targetName}'...");
            _fileSystem.CopyFileIn(sourcePath, targetName);
            Console.WriteLine("File copied successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying file: {ex.Message}");
        }
    }

    static void CopyFileOut(string fileName, string targetPath)
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }
        if (TextUtils.IsNullOrWhiteSpace(fileName) || TextUtils.IsNullOrWhiteSpace(targetPath))
        {
            Console.WriteLine("File name and target path must not be empty.");
            return;
        }
        try
        {
            Console.WriteLine($"Copying '{fileName}' from container to '{targetPath}'...");
            _fileSystem.CopyFileOut(fileName, targetPath);
            Console.WriteLine("File copied successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying file out: {ex.Message}");
        }
    }

    static void DeleteFile(string fileName)
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }
        if (TextUtils.IsNullOrWhiteSpace(fileName))
        {
            Console.WriteLine("File name must not be empty.");
            return;
        }
        try
        {
            Console.WriteLine($"Deleting file '{fileName}'...");
            _fileSystem.DeleteFile(fileName);
            Console.WriteLine("File deleted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting file: {ex.Message}");
        }
    }

    static void CreateDirectory(string directoryName)
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }
        if (TextUtils.IsNullOrWhiteSpace(directoryName))
        {
            Console.WriteLine("Directory name must not be empty.");
            return;
        }
        try
        {
            Console.WriteLine($"Creating directory '{directoryName}'...");
            _fileSystem.CreateDirectory(directoryName);
            Console.WriteLine("Directory created successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating directory: {ex.Message}");
        }
    }

    static void ChangeDirectory(string directoryName)
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }
        if (TextUtils.IsNullOrWhiteSpace(directoryName))
        {
            Console.WriteLine("Directory name must not be empty.");
            return;
        }
        try
        {
            Console.WriteLine($"Changing to directory '{directoryName}'...");
            _fileSystem.ChangeDirectory(directoryName);
            Console.WriteLine($"Current directory: {_fileSystem.GetCurrentPath()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error changing directory: {ex.Message}");
        }
    }

    static void RemoveDirectory(string directoryName)
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }
        if (TextUtils.IsNullOrWhiteSpace(directoryName))
        {
            Console.WriteLine("Directory name must not be empty.");
            return;
        }
        try
        {
            Console.WriteLine($"Removing directory '{directoryName}'...");
            _fileSystem.RemoveDirectory(directoryName);
            Console.WriteLine("Directory removed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing directory: {ex.Message}");
        }
    }

    static void ShowContainerInfo()
    {
        if (_fileSystem == null)
        {
            Console.WriteLine("File system not initialized.");
            return;
        }
        try
        {
            var info = _fileSystem.GetContainerInfo();
            Console.WriteLine("\nContainer Information:");
            Console.WriteLine($"  Path: {info.Path}");
            Console.WriteLine($"  Block Size: {info.BlockSize} bytes");
            Console.WriteLine($"  Total Blocks: {info.TotalBlocks}");
            Console.WriteLine($"  Used Blocks: {info.UsedBlocks}");
            Console.WriteLine($"  Free Blocks: {info.FreeBlocks}");
            Console.WriteLine($"  Current Directory: {info.CurrentDirectory}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving container info: {ex.Message}");
        }
    }
}
