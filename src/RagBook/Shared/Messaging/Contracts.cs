namespace RagBook.Shared.Messaging;

/// <summary>Marker for a write with no result payload.</summary>
public interface ICommand;

/// <summary>Marker for a write producing <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">The result payload type.</typeparam>
public interface ICommand<TResult>;

/// <summary>Marker for a read producing <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">The result payload type.</typeparam>
public interface IQuery<TResult>;

/// <summary>Marker for an internal/domain event published in-process.</summary>
public interface IEvent;

/// <summary>Marker for an integration event routed to the durable outbox.</summary>
public interface IExternalEvent;
