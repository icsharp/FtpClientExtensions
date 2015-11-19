using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.FtpClient;
using System.Text;

namespace icsharp.Common.Utility
{
	public static class FtpExtensions
	{
		// 2M bytes.
		private const int MaxCacheSize = 2097152;
		// 2K bytes.
		private const int BufferSize = 2048;

		public static void Download(this FtpClient conn, FtpListItem file, string localFile)
		{
			if (file.Type != FtpFileSystemObjectType.File) throw new FtpException("invaild file");

			using (var responseStream = conn.OpenRead(file.FullName))
			{
				// Cache data in memory.
				using (var downloadCache = new MemoryStream(MaxCacheSize))
				{
					int bytesSize = 0;
					int cachedSize = 0;
					byte[] downloadBuffer = new byte[BufferSize];

					// Download the file until the download is completed.
					while (true)
					{
						// Read a buffer of data from the stream.
						bytesSize = responseStream.Read(downloadBuffer, 0,
							downloadBuffer.Length);

						// If the cache is full, or the download is completed, write 
						// the data in cache to local file.
						if (bytesSize == 0
							|| MaxCacheSize < cachedSize + bytesSize)
						{
							// Write the data in cache to local file.
							WriteCacheToFile(downloadCache, localFile, cachedSize);

							// Stop downloading the file if the download is paused, 
							// canceled or completed. 
							if (bytesSize == 0)
							{
								break;
							}

							// Reset cache.
							downloadCache.Seek(0, SeekOrigin.Begin);
							cachedSize = 0;

						}

						// Write the data from the buffer to the cache in memory.
						downloadCache.Write(downloadBuffer, 0, bytesSize);
						cachedSize += bytesSize;
					}
				}
			}
		}
		public static void DownloadDirectory(this FtpClient conn, string serverDirectory, string localPath)
		{
			if (!Directory.Exists(localPath)) Directory.CreateDirectory(localPath);

			conn.SetWorkingDirectory(serverDirectory);

			Debug.WriteLine("the current dir is:{0}", conn.GetWorkingDirectory());

			foreach (var fileSystem in conn.GetListing())
			{
				if (fileSystem.Type == FtpFileSystemObjectType.Directory)
				{
					DownloadDirectory(conn, fileSystem.FullName, Path.Combine(localPath, fileSystem.Name));
				}
				else if (fileSystem.Type == FtpFileSystemObjectType.File)
				{
					Download(conn, fileSystem, Path.Combine(localPath, fileSystem.Name));
				}
				else
				{
					Debug.WriteLine("{0} symbolic link downloading is skipped.", fileSystem.FullName);
				}
			}
		}
		private static void WriteCacheToFile(MemoryStream downloadCache, string localFile, int cachedSize)
		{
			using (FileStream fileStream = new FileStream(localFile, FileMode.Append))
			{
				byte[] cacheContent = new byte[cachedSize];
				downloadCache.Seek(0, SeekOrigin.Begin);
				downloadCache.Read(cacheContent, 0, cachedSize);
				fileStream.Write(cacheContent, 0, cachedSize);
			}
		}
		private static void Upload(this FtpClient conn, string fileUri, FileInfo localFile)
		{
			if (localFile == null) throw new ArgumentNullException(" The file to upload is null. ");

			using (var requestStream = conn.OpenWrite(fileUri))
			{
				// Open the local file to read.
				using (var localFileStream = localFile.OpenRead())
				{
					int bytesSize = 0;
					byte[] uploadBuffer = new byte[BufferSize];
					while (true)
					{
						// Read a buffer of local file.
						bytesSize = localFileStream.Read(uploadBuffer, 0, uploadBuffer.Length);

						if (bytesSize == 0)
						{
							break;
						}
						else
						{
							// Write the buffer to the request stream.
							requestStream.Write(uploadBuffer, 0, bytesSize);
						}
					}

				}
			}


		}
		public static void Upload(this FtpClient conn, string serverDirectory, params string[] files)
		{
			if (files == null) throw new ArgumentNullException("files");
			Upload(conn, serverDirectory, files.Select(file => new FileInfo(file)).ToArray());
		}
		public static void Upload(this FtpClient conn, string serverDirectory, params FileInfo[] files)
		{
			if (files == null) throw new ArgumentNullException("files");
			if (!conn.DirectoryExists(serverDirectory)) throw new FtpException(string.Format("{0} not exists", serverDirectory));
			foreach (var file in files)
			{
				var fileUri = string.Format("{0}/{1}", serverDirectory, file.Name);
				Upload(conn, fileUri, file);
			}
		}
		public static void UploadDirectory(this FtpClient conn, string serverDirectory, string localDirectory, bool createFolderOnServer)
		{
			if (!Directory.Exists(localDirectory)) throw new DirectoryNotFoundException(localDirectory);
			UploadDirectory(conn, serverDirectory, new DirectoryInfo(localDirectory), createFolderOnServer);
		}
		public static void UploadDirectory(this FtpClient conn, string serverDirectory, DirectoryInfo localDirectory, bool createFolderOnServer)
		{
			if (localDirectory == null) throw new ArgumentNullException("localDirectory");
			if (!conn.DirectoryExists(serverDirectory)) throw new FtpException(string.Format("{0} not exists", serverDirectory));

			// The method UploadDirectoriesAndFiles will create or override a folder by default.
			if (createFolderOnServer)
			{
				UploadDirectoriesAndFiles(conn, serverDirectory, new FileSystemInfo[] { localDirectory });
			}
			// Upload the files and sub directories of the local folder.
			else
			{
				UploadDirectoriesAndFiles(conn, serverDirectory, localDirectory.GetFileSystemInfos());
			}
		}
		private static void UploadDirectoriesAndFiles(this FtpClient conn, string serverDirectory, IEnumerable<FileSystemInfo> fileSysInfos)
		{
			foreach (var fileSys in fileSysInfos)
			{
				UploadDirectoryOrFile(conn, serverDirectory, fileSys);
			}
		}
		private static void UploadDirectoryOrFile(this FtpClient conn, string serverDirectory, FileSystemInfo fileSystem)
		{
			// Upload the file directly.
			if (fileSystem is FileInfo)
			{
				var fileUri = string.Format("{0}/{1}", serverDirectory, fileSystem.Name);
				Upload(conn, fileUri, fileSystem as FileInfo);
			}
			// Upload a directory.
			else
			{
				// Construct the sub directory Uri.
				var subDirectoryPath = string.Format("{0}/{1}", serverDirectory, fileSystem.Name);
				if (!conn.DirectoryExists(subDirectoryPath)) conn.CreateDirectory(subDirectoryPath);

				// Get the sub directories and files.
				var subDirectoriesAndFiles = (fileSystem as DirectoryInfo).GetFileSystemInfos();
				// Upload the files in the folder and sub directories.
				foreach (var subFile in subDirectoriesAndFiles)
				{
					UploadDirectoryOrFile(conn, subDirectoryPath, subFile);
				}
			}
		}
		public static void DeleteSubDirectory(this FtpClient conn, string parentDirectory)
		{
			if (string.IsNullOrEmpty(parentDirectory)) throw new ArgumentNullException("parentDirectory");
			if (!conn.DirectoryExists(parentDirectory)) throw new FtpException(string.Format("{0} not exists.", parentDirectory));

			foreach (FtpListItem item in conn.GetListing(parentDirectory))
			{
				switch (item.Type)
				{
					case FtpFileSystemObjectType.File:
						conn.DeleteFile(item.FullName);
						break;
					case FtpFileSystemObjectType.Directory:
						conn.DeleteDirectory(item.FullName, true);
						break;
					default:
						Debug.WriteLine("Don't know how to delete object type: " + item.Type);
						break;
				}
			}
		}
	}
}
