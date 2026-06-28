using System.Collections.Generic;

namespace Conquer.World
{
    /// <summary>
    /// The result of a <see cref="MapInstance.Move"/> across a cell boundary: who newly
    /// entered the mover's 3x3 screen block (=&gt; mutual 1014 spawn) and who left it
    /// (=&gt; RemoveEntity 132). A within-cell step produces <see cref="Empty"/>.
    /// </summary>
    public readonly record struct ScreenDiff(
        IReadOnlyList<IWorldEntity> Entered,
        IReadOnlyList<IWorldEntity> Left)
    {
        /// <summary>No enter/leave change (within-cell move).</summary>
        public static ScreenDiff Empty { get; } =
            new(System.Array.Empty<IWorldEntity>(), System.Array.Empty<IWorldEntity>());
    }
}
