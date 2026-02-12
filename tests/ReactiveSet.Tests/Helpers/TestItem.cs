namespace com.hollerson.reactivesets.tests;

public record TestUser(int Id, string Name, string Department);

public record TestOrder(int Id, int CustomerId, decimal Total);

public record TestCustomer(int Id, string Name);

public record NamedItem(int Id, string Value);

public record LineItem(int Id, string Product, int Quantity);

public record Wrapper<T>(T Inner) where T : class;
