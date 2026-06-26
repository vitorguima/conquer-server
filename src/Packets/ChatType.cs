namespace Conquer.Packets
{
    /// <summary>
    /// Minimal CO chat-channel enum. Only <see cref="Entrance"/> (2101) is needed for
    /// the character-select flow (ANSWER_OK / NEW_ROLE). Derived from the original
    /// <c>enum ChatType : ushort</c>: Talk=2000, Register=Talk+100=2100, Entrance(next)=2101.
    /// </summary>
    public enum ChatType : ushort
    {
        Entrance = 2101
    }
}
