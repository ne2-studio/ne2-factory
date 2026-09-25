namespace Ne2Factory.Cli;

// DI wrapper around the root dir resolved before the host was built (see
// RootDirectoryResolver), so services can depend on it like anything else.
internal sealed record RootDirectory(string Path);
