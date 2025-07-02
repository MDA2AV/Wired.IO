using System.Buffers;

namespace Wired.IO.Utilities;

public static class PipeReaderUtilities
{
    /// <summary>
    /// Searches for a specific byte sequence in a ReadOnlySequence and advances to that position.
    /// </summary>
    /// <param name="reader">The sequence reader to search within.</param>
    /// <param name="delimiter">The byte sequence to find.</param>
    /// <param name="position">
    /// When this method returns, contains the position immediately after the delimiter
    /// if found; otherwise, the current position.
    /// </param>
    /// <returns>
    /// true if the delimiter was found; otherwise, false.
    /// </returns>
    /// <remarks>
    /// This method handles searching across multiple segments of a ReadOnlySequence,
    /// making it suitable for finding delimiters that might span segment boundaries.
    /// When the delimiter is found, the reader is advanced to the position immediately
    /// after the delimiter.
    /// </remarks>
    public static bool TryAdvanceTo(SequenceReader<byte> reader, ReadOnlySpan<byte> delimiter, out SequencePosition position)
    {
        // Start from the current position
        position = reader.Position;

        // Continue until we reach the end of the sequence
        while (!reader.End)
        {
            // Get the current unread span (current segment)
            var span = reader.UnreadSpan;

            // Try to find the delimiter in the current span
            var index = span.IndexOf(delimiter);
            if (index != -1)
            {
                // Delimiter found - calculate the position after the delimiter
                position = reader.Sequence.GetPosition(index + delimiter.Length, reader.Position);

                // Move the reader past the delimiter
                reader.Advance(index + delimiter.Length);

                return true;
            }

            // Move to the next segment if not found in the current span
            reader.Advance(span.Length);
        }

        // Delimiter not found in the entire sequence
        return false;
    }
}