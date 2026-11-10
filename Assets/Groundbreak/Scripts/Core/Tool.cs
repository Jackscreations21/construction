namespace Groundbreak
{
    /// <summary>Base class for everything that goes on the tool belt. Plain C# object; ToolBelt drives it.
    /// ToolBelt calls these only when !Game.UIOpen.</summary>
    public abstract class Tool
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public virtual int Slots => 1;           // big tools take 2 belt slots
        public virtual bool Cordless => false;
        public float Battery = 1f;               // 0..1, only meaningful if Cordless
        public float Durability = 1f;            // 0..1; 0 = broken

        public virtual void OnEquip() { }
        public virtual void OnUnequip() { }
        public virtual void Primary() { }        // left mouse pressed this frame
        public virtual void PrimaryHeld() { }    // left mouse held (every frame)
        public virtual void Secondary() { }      // right mouse pressed this frame
        public virtual void Tick() { }           // every frame while equipped
        public virtual string Hint => "";        // one-line usage hint shown by HUD
        public virtual void DrawGUI() { }        // optional extra OnGUI drawing while equipped
    }
}
