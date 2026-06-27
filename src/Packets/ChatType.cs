namespace Conquer.Packets
{
    /// <summary>
    /// Minimal CO chat-channel enum. <see cref="Talk"/> (2000) is the local screen channel
    /// (3x3 fan-out). <see cref="Entrance"/> (2101) is needed for the character-select flow
    /// (ANSWER_OK / NEW_ROLE). Derived from the original <c>enum ChatType : ushort</c>:
    /// Talk=2000, Register=Talk+100=2100, Entrance(next)=2101.
    /// </summary>
    public enum ChatType : ushort
    {
        Talk = 2000,
        Register = 2100,
        Entrance = 2101
    }
}
