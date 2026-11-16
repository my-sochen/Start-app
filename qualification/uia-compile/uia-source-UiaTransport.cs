// SOURCE-ONLY: not compiled locally because WindowsDesktop reference bytes are absent.
// No entrypoint. Never instantiated by portable tests or transport-backed assembly.
using System.Windows.Automation;
using StartApp.Observation.Windows;
namespace StartApp.Observation.Uia;

public sealed class UiaTransport:IWindowsTransport {
 readonly int thread=Environment.CurrentManagedThreadId;bool disposed;
 readonly HashSet<AutomationElement> owned=new(ReferenceEqualityComparer.Instance);
 // Raw direct-desktop navigation is a proposed replacement requiring measurement.
 readonly TreeWalker raw;
 public UiaTransport(){if(!OperatingSystem.IsWindows()||Thread.CurrentThread.GetApartmentState()!=ApartmentState.MTA)throw new InvalidOperationException("Dedicated Windows MTA required");raw=new TreeWalker(Condition.TrueCondition);}
 void Live(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Transport thread mismatch");if(disposed)throw new ObjectDisposedException(nameof(UiaTransport));}
 AutomationElement Element(object value){Live();if(value is not AutomationElement element||!owned.Contains(element))throw new InvalidOperationException("Foreign transport element");return element;}
 object? Own(AutomationElement? element){if(element is not null)owned.Add(element);return element;}
 static AutomationProperty Property(PropertyKey key)=>key switch {
 PropertyKey.ProcessId=>AutomationElement.ProcessIdProperty,PropertyKey.ControlType=>AutomationElement.ControlTypeProperty,PropertyKey.Name=>AutomationElement.NameProperty,PropertyKey.ClassName=>AutomationElement.ClassNameProperty,PropertyKey.AutomationId=>AutomationElement.AutomationIdProperty,PropertyKey.Hwnd=>AutomationElement.NativeWindowHandleProperty,
 PropertyKey.ValueAvailable=>AutomationElement.IsValuePatternAvailableProperty,PropertyKey.ToggleAvailable=>AutomationElement.IsTogglePatternAvailableProperty,PropertyKey.Value=>ValuePattern.ValueProperty,PropertyKey.ToggleState=>TogglePattern.ToggleStateProperty,_=>throw new ArgumentOutOfRangeException(nameof(key))};
 static AutomationPattern Pattern(PatternKey key)=>key switch{PatternKey.Value=>ValuePattern.Pattern,PatternKey.Toggle=>TogglePattern.Pattern,_=>throw new ArgumentOutOfRangeException(nameof(key))};
 static CacheRequest Request(CacheStage stage){
 if(stage.Scope!=CacheScope.Element||stage.Mode!=CacheMode.Full||stage.Filter!=CacheFilter.Raw)throw new ArgumentException("Unsupported cache policy");
 var request=new CacheRequest{TreeScope=TreeScope.Element,TreeFilter=Condition.TrueCondition,AutomationElementMode=AutomationElementMode.Full};
 foreach(var property in stage.Properties)request.Add(Property(property));foreach(var pattern in stage.Patterns)request.Add(Pattern(pattern));return request;
 }
 TreeWalker Walker(NavigationView view)=>view switch{NavigationView.RawDesktopChildren=>raw,NavigationView.ControlDescendants=>TreeWalker.ControlViewWalker,_=>throw new ArgumentOutOfRangeException(nameof(view))};
 public object DesktopRoot(){Live();return Own(AutomationElement.RootElement)!;}
 public object? FirstChild(object element,NavigationView view,CacheStage stage){var e=Element(element);return Own(Walker(view).GetFirstChild(e,Request(stage)));}
 public object? NextSibling(object element,NavigationView view,CacheStage stage){var e=Element(element);return Own(Walker(view).GetNextSibling(e,Request(stage)));}
 public object Refresh(object element,CacheStage stage){var e=Element(element);return Own(e.GetUpdatedCache(Request(stage)))!;}
 public TransportValue CachedProperty(object cached,PropertyKey key,bool ignoreDefaultValue){
 var e=Element(cached);if(!ignoreDefaultValue)throw new ArgumentException("Default substitution forbidden");
 try{object value=e.GetCachedPropertyValue(Property(key),true);if(ReferenceEquals(value,AutomationElement.NotSupported))return new(TransportStatus.Unsupported,null);
 if(key==PropertyKey.ControlType){if(value is not ControlType c)throw new InvalidOperationException("Wrong ControlType property");value=new ControlTypeData(c.Id,c.ProgrammaticName);}
 if(key==PropertyKey.ToggleState){if(value is not ToggleState state)throw new InvalidOperationException("Wrong ToggleState property");value=(int)state;}
 return TransportValue.Present(value);
 }catch(ElementNotAvailableException){return new(TransportStatus.Failed,null);}catch(InvalidOperationException){return new(TransportStatus.Failed,null);}
 }
 public TransportValue CachedPattern(object cached,PatternKey key){var e=Element(cached);try{
 if(!e.TryGetCachedPattern(Pattern(key),out object pattern)||pattern is null)return new(TransportStatus.Failed,null);
 return TransportValue.Present(pattern is ValuePattern?ActualPattern.Value:pattern is TogglePattern?ActualPattern.Toggle:ActualPattern.Other);
 }catch(ElementNotAvailableException){return new(TransportStatus.Failed,null);}catch(InvalidOperationException){return new(TransportStatus.Failed,null);}}
 public void Dispose(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Transport thread mismatch");if(disposed)return;disposed=true;owned.Clear();}
}
