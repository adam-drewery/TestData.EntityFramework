namespace EntityFramework.TestData;

public interface ISeed<out T> where T : class
{
    IEnumerable<T> Create();
    
    public Type EntityType { get; }
}