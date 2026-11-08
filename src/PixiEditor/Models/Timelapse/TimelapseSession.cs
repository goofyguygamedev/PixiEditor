using System.IO;
using System.Linq;
using Drawie.Numerics;

namespace PixiEditor.Models.Timelapse;

/// <summary>
/// One document's in-progress timelapse recording: where its frames live on disk, how many have
/// been written, and which document change was captured last.
/// </summary>
/// <remarks>
/// Frames are kept as individual PNGs on disk rather than in memory. A long drawing session can
/// produce thousands of frames, and holding them all in RAM at canvas resolution is not viable;
/// on-disk frames also survive a crash, which is the case a timelapse is least forgiving about.
/// </remarks>
internal class TimelapseSession
{
    public Guid DocumentId { get; }

    public string FolderPath { get; }

    public int FrameCount { get; private set; }

    /// <summary>Size of the most recently written frame, used to configure the encoder.</summary>
    public VecI FrameSize { get; private set; }

    /// <summary>
    /// The document's change id at the time the last frame was written. Used to skip idle
    /// intervals, so time spent not drawing does not pad the replay.
    /// </summary>
    public Guid? LastCapturedChangeGuid { get; set; }

    public TimelapseSession(Guid documentId, string folderPath)
    {
        DocumentId = documentId;
        FolderPath = folderPath;
        FrameCount = CountExistingFrames(folderPath);
    }

    public string NextFramePath() => Path.Combine(FolderPath, $"frame_{FrameCount:D6}.png");

    public void OnFrameWritten(VecI size)
    {
        FrameCount++;
        FrameSize = size;
    }

    /// <summary>Frame files in capture order. Zero-padded names make ordinal sorting correct.</summary>
    public IEnumerable<string> EnumerateFramePaths() =>
        Directory.Exists(FolderPath)
            ? Directory.EnumerateFiles(FolderPath, "frame_*.png").OrderBy(p => p, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    public void DeleteFrames()
    {
        if (!Directory.Exists(FolderPath))
        {
            FrameCount = 0;
            return;
        }

        foreach (string path in EnumerateFramePaths().ToList())
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A frame we cannot delete is not worth failing the reset over.
            }
        }

        FrameCount = 0;
        LastCapturedChangeGuid = null;
    }

    /// <summary>
    /// Resumes numbering after a restart so an existing folder is appended to rather than
    /// overwritten from frame zero.
    /// </summary>
    private static int CountExistingFrames(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return 0;

        return Directory.EnumerateFiles(folderPath, "frame_*.png").Count();
    }
}
