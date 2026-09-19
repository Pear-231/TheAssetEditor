namespace Editors.Audio.Shared.Wwise.Engine.Dsp
{
    // Nudges a seek onto the point where the waveform continues rather than restarts.
    //
    // Cross-fading between two arbitrary points of the same media sums two copies of a correlated
    // signal at different phases, and over the fade the resultant phase slews from one to the other.
    // On a pure tone that is a momentary pitch bend: measured at 76 frames per cycle against a
    // steady 109 on a 440 Hz tone, which is an instantaneous 630 Hz. Lengthening the fade shrinks it
    // proportionally but never removes it, because the phase difference being slewed is unchanged.
    //
    // Removing it means not having a phase difference to slew. This searches a window around the
    // requested position for the offset whose samples best continue the ones currently playing, and
    // seeks there instead -- the same idea as the overlap-add used by time-stretchers. The seek
    // lands within half a period of where it was asked to, which is inaudible as a position error
    // and is the difference between a click-free join and a phase-continuous one.
    //
    // Real-time safe by construction: no allocation, and the work is fixed by the two constants
    // below whatever the media is.
    internal static class SeekAlignment
    {
        // How far either side of the requested position to look. One period of 60 Hz at 48 kHz, so
        // anything with pitch above the bottom of hearing has a matching phase inside the window.
        private const int SearchRadiusFrames = 400;

        // How much history to match on. Long enough to span a period of everything but the very
        // lowest content, short enough that the search stays cheap: 512 by 800 offsets is about
        // 400,000 multiply-adds, well under a millisecond, and it happens only on a seek.
        private const int ComparisonFrames = 512;

        // Returns the frame to seek to: the requested one, nudged to where the waveform continues.
        //
        // Falls back to the requested frame whenever alignment cannot mean anything -- too close to
        // either end of the media to compare, or nothing currently playing to continue from.
        public static double AlignToWaveform(
            ReadOnlySpan<float> samples,
            int channelCount,
            double currentSourceFrame,
            double requestedSourceFrame)
        {
            var frameCount = samples.Length / channelCount;
            var currentFrame = (int)currentSourceFrame;
            var requestedFrame = (int)requestedSourceFrame;

            // The history being continued has to exist, and every candidate has to be comparable.
            if (currentFrame < ComparisonFrames || currentFrame >= frameCount)
                return requestedSourceFrame;

            var firstCandidate = requestedFrame - SearchRadiusFrames;
            var lastCandidate = requestedFrame + SearchRadiusFrames;
            if (firstCandidate < ComparisonFrames || lastCandidate >= frameCount)
                return requestedSourceFrame;

            var historyStart = (currentFrame - ComparisonFrames) * channelCount;
            var bestCorrelation = float.NegativeInfinity;
            var bestFrame = requestedFrame;

            for (var candidate = firstCandidate; candidate <= lastCandidate; candidate++)
            {
                var candidateStart = (candidate - ComparisonFrames) * channelCount;
                var correlation = 0f;
                for (var offset = 0; offset < ComparisonFrames; offset++)
                {
                    // The first channel alone. A seek is a decision about time, and the channels of
                    // one source share their timing; correlating all of them would cost a multiple
                    // for an answer that does not change.
                    correlation += samples[historyStart + offset * channelCount]
                        * samples[candidateStart + offset * channelCount];
                }

                if (correlation <= bestCorrelation)
                    continue;
                bestCorrelation = correlation;
                bestFrame = candidate;
            }

            // Silence correlates with silence at every offset, and picking the first of those is
            // arbitrary rather than aligned. Nothing to continue means nothing to align to.
            if (bestCorrelation <= 0f)
                return requestedSourceFrame;

            // The fractional part of the request is kept: the alignment moves whole frames, and
            // throwing away the fraction would put back a sub-sample discontinuity.
            return bestFrame + (requestedSourceFrame - requestedFrame);
        }
    }
}
