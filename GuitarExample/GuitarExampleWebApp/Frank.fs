﻿(* # Frank

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2012, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
[<AutoOpen>]
module Frank.Core

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Text
open FSharpx
open FSharpx.Reader

#if DEBUG
open Newtonsoft.Json.Linq
open NUnit.Framework
open Swensen.Unquote.Assertions
#endif

// ## Define the web application interface

(*
One may define a web application interface using a large variety of signatures. Indeed, if you search the web, you're likely to find a large number of approaches. When starting with `Frank`, I wanted to try to find a way to define an HTTP application using pure functions and function composition. The closest I found was the following:

    type HttpApplication = HttpRequestMessage -> Async<HttpResponseMessage>
    
    let orElse left right = fun request -> Option.orElse (left request) (right request)
    let inline (<|>) left right = orElse left right 

These signatures represent both the application signature and a means for merging multiple applications together into a single application. This allowed for a nice symmetry and elegance in that everything you composed would always have the same signature. Additional functions allow you to map applications to specific methods or uri patterns.

Alas, this approach works only so well. HTTP is a rich communication specification. The simplicity and elegance of a purely functional approach quickly loses the ability to communicate back options to the client. For instance, given the above, how do you return a meaningful `405 Method Not Allowed` response? The HTTP specification requires that you list the allowed methods, but if you merge all the logic for selecting an application into the functions, there is no easy way to recall all the allowed methods, short of trying them all. You could require that the developer add the list of used methods, but that, too, misses the point that the application should be collecting this and helping the developer by taking care of all of the nuts and bolts items.

The next approach I tried involved using a tuple of a list of allowed HTTP methods and the application handler, which used the merged function approach described above for actually executing the application. However, once again, there are limitations. This structure accurately represents a resource, but it does not allow for multiple resources to coexist side-by-side. Another tuple of uri pattern matching expressions could wrap a list of these method * handler tuples, but at this point I realized I would be better served by using real types and thus arrived at the signatures below.

You'll see the signatures above are still mostly present, though they have been changed to better fit the signatures below.
*)

// `HttpApplication` defines the contract for processing any request.
// An application takes an `HttpRequestMessage` and returns an `HttpRequestHandler` asynchronously.
type HttpApplication = HttpRequestMessage -> Async<HttpResponseMessage>

// ## HTTP Response Header Combinators

// Headers are added using the `Reader` monad. If F# allows mutation, why do we need the monad?
// First of all, it allows for the explicit declaration of side effects. Second, a number
// of combinators are already defined that allows you to more easily compose headers.
type HttpResponseHeadersBuilder = Reader<HttpResponseMessage, unit>
let respond statusCode headers content =
  let response = new HttpResponseMessage(statusCode, Content = content) in
  headers response; response

// ### General Headers
let Date x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Date <- Nullable.create x

let Connection x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Connection.ParseAdd x

let Trailer x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Trailer.ParseAdd x

let ``Transfer-Encoding`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.TransferEncoding.ParseAdd x

let Upgrade x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Upgrade.ParseAdd x

let Via x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Via.ParseAdd x

let ``Cache-Control`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.CacheControl <- CacheControlHeaderValue.Parse x

let Pragma x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Pragma.ParseAdd x

// ### Response Headers
let Age x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Age <- Nullable.create x

let ``Retry-After`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.RetryAfter <- RetryConditionHeaderValue.Parse x

let Server x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Server.ParseAdd x

let Warning x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Warning.ParseAdd x

let ``Accept-Ranges`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.AcceptRanges.ParseAdd x

let Vary x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Vary.ParseAdd x

let ``Proxy-Authenticate`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.ProxyAuthenticate.ParseAdd x

let ``WWW-Authenticate`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.WwwAuthenticate.ParseAdd x

// ### Entity Headers
let Allow x : HttpResponseHeadersBuilder =
  fun response -> Seq.iter response.Content.Headers.Allow.Add x

let Location x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Location <- x

let ``Content-Disposition`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentDisposition <- ContentDispositionHeaderValue x

let ``Content-Encoding`` x : HttpResponseHeadersBuilder =
  fun response -> Seq.iter response.Content.Headers.ContentEncoding.Add x

let ``Content-Language`` x : HttpResponseHeadersBuilder =
  fun response -> Seq.iter response.Content.Headers.ContentLanguage.Add x 

let ``Content-Length`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentLength <- Nullable.create x

let ``Content-Location`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentLocation <- x

let ``Content-MD5`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentMD5 <- x

let ``Content-Range`` from _to length : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentRange <- ContentRangeHeaderValue(from, _to, length)

let ``Content-Type`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentType <- MediaTypeHeaderValue x

let ETag tag isWeak : HttpResponseHeadersBuilder =
  fun response -> response.Headers.ETag <- EntityTagHeaderValue(tag, isWeak)

let Expires x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.Expires <- Nullable.create x

let ``Last Modified`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.LastModified <- Nullable.create x

// Response helpers - shortcuts for common responses.
let OK headers content = respond HttpStatusCode.OK headers content

#if DEBUG
open Newtonsoft.Json.Linq
open NUnit.Framework
open Swensen.Unquote.Assertions

[<Test>]
let ``test respond without body``() =
  let response = respond HttpStatusCode.OK ignore HttpContent.Empty
  test <@ response.StatusCode = HttpStatusCode.OK @>
  test <@ response.Content = HttpContent.Empty @>

[<Test>]
let ``test respond with StringContent``() =
  let body = "Howdy"
  let response = OK ignore <| new StringContent(body)
  test <@ response.StatusCode = HttpStatusCode.OK @>
  test <@ response.Content.ReadAsStringAsync().Result = body @>

[<Test>]
let ``test respond with negotiated body``() =
  let body = "Howdy"
  let response = OK ignore <| new ObjectContent<_>(body, new XmlMediaTypeFormatter(), "text/plain")
  test <@ response.StatusCode = HttpStatusCode.OK @>
  test <@ response.Content.ReadAsStringAsync().Result = @"<string xmlns=""http://schemas.microsoft.com/2003/10/Serialization/"">Howdy</string>" @>
#endif

// ### Allow Header Helpers

// A few responses should return allowed methods (`OPTIONS` and `405 Method Not Allowed`).
// `respondWithAllowHeader` allows both methods to share common functionality.
let private respondWithAllowHeader statusCode allowedMethods body =
  fun _ -> async {
    return respond statusCode <| Allow allowedMethods <| body }

// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
let options allowedMethods =
  respondWithAllowHeader HttpStatusCode.OK allowedMethods HttpContent.Empty

#if DEBUG
[<Test>]
let ``test options``() =
  let response = options ["GET";"POST"] (new HttpRequestMessage()) |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.OK @>
  test <@ response.Content.Headers.Allow.Contains("GET") @>
  test <@ response.Content.Headers.Allow.Contains("POST") @>
  test <@ not <| response.Content.Headers.Allow.Contains("PUT") @>
  test <@ not <| response.Content.Headers.Allow.Contains("DELETE") @>
#endif

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
  respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods
  <| new StringContent("405 Method Not Allowed")

#if DEBUG
[<Test>]
let ``test 405 Method Not Allowed``() =
  let response = ``405 Method Not Allowed`` ["GET";"POST"] (new HttpRequestMessage()) |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.MethodNotAllowed @>
  test <@ response.Content.Headers.Allow.Contains("GET") @>
  test <@ response.Content.Headers.Allow.Contains("POST") @>
  test <@ not <| response.Content.Headers.Allow.Contains("PUT") @>
  test <@ not <| response.Content.Headers.Allow.Contains("DELETE") @>
#endif

// ## Content Negotiation Helpers

let ``406 Not Acceptable`` =
  fun _ -> async {
    return respond HttpStatusCode.NotAcceptable ignore <| new StringContent("406 Not Acceptable") }

#if DEBUG
[<Test>]
let ``test 406 Not Acceptable``() =
  let response = ``406 Not Acceptable`` <| new HttpRequestMessage() |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.NotAcceptable @>
#endif

let findFormatterFor mediaType =
  Seq.find (fun (formatter: MediaTypeFormatter) ->
    formatter.SupportedMediaTypes
    |> Seq.map (fun value -> value.MediaType)
    |> Seq.exists ((=) mediaType))

// `formatWith` allows you to specify a specific `formatter` with which to render a representation
// of your content body.
// 
// The `Web API` tries to do this for you at this time, so this function is likely to be clobbered,
// or rather, wrapped again in another representation. Hopefully, this will get fixed in a future release.
// 
// Further note that the current solution requires creation of `ObjectContent<_>`, which is certainly
// not optimal. Hopefully this, too, will be resolved in a future release.
let formatWith (mediaType: string) formatter body =
  new ObjectContent<_>(body, formatter, mediaType) :> HttpContent

#if DEBUG
type TestType() =
  let mutable firstName = ""
  let mutable lastName = ""
  member x.FirstName
    with get() = firstName
    and set(v) = firstName <- v
  member x.LastName
    with get() = lastName
    and set(v) = lastName <- v
  override x.ToString() = firstName + " " + lastName

[<Test>]
let ``test formatWith properly format as application/json raw``() =
  let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley")
  let content = new ObjectContent<_>(body, formatter, "application/json") :> HttpContent
  test <@ content.Headers.ContentType.MediaType = "application/json" @>
  let result = content.ReadAsStringAsync()
               |> Async.AwaitTask
               |> Async.RunSynchronously
  test <@ result = "{\"firstName\":\"Ryan\",\"lastName\":\"Riley\"}" @>

[<Test>]
let ``test formatWith properly format as application/json``() =
  let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley")
  let content = body |> formatWith "application/json" formatter
  test <@ content.Headers.ContentType.MediaType = "application/json" @>
  let result = content.AsyncReadAsString() |> Async.RunSynchronously
  test <@ result = "{\"firstName\":\"Ryan\",\"lastName\":\"Riley\"}" @>

[<Test>]
let ``test formatWith properly format as application/xml``() =
  let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley")
  let content = body |> formatWith "application/xml" formatter
  test <@ content.Headers.ContentType.MediaType = "application/xml" @>
  let result = content.AsyncReadAsString() |> Async.RunSynchronously
  test <@ result = @"<Core.TestType xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://schemas.datacontract.org/2004/07/Frank""><firstName>Ryan</firstName><lastName>Riley</lastName></Core.TestType>" @>

[<Test>]
let ``test formatWith properly format as application/xml and read as TestType``() =
  let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley")
  let content = body |> formatWith "application/xml" formatter
  test <@ content.Headers.ContentType.MediaType = "application/xml" @>
  let result = content.AsyncReadAs<TestType>() |> Async.RunSynchronously
  test <@ result.FirstName = body.FirstName && result.LastName = body.LastName @>
#endif

let IO stream = new StreamContent(stream) :> HttpContent
let Str s = new StringContent(s) :> HttpContent
let Formatted (s, encoding, mediaType) = new StringContent(s, encoding, mediaType) :> HttpContent
let Form pairs = new FormUrlEncodedContent(pairs |> Seq.map (fun (k,v) -> new KeyValuePair<_,_>(k,v))) :> HttpContent
let Bytes bytes = new ByteArrayContent(bytes) :> HttpContent
let Segment (segment: ArraySegment<byte>) =
  new ByteArrayContent(segment.Array, segment.Offset, segment.Count) :> HttpContent

type internal ConnegFormatter(mediaType) as x =
  inherit MediaTypeFormatter()
  do x.SupportedMediaTypes.Add(MediaTypeHeaderValue(mediaType))
  override x.CanReadType(_) = true
  override x.CanWriteType(_) = true

let negotiateMediaType formatters request =
  let negotiator = new System.Net.Http.Formatting.DefaultContentNegotiator()
  match negotiator.Negotiate(typeof<obj>, request, formatters) with
  | null -> None
  | result -> Some(result.MediaType.MediaType)

// When you want to negotiate the format of the response based on the available representations and
// the `request`'s `Accept` headers, you can `tryNegotiateMediaType`. This takes a set of available
// `formatters` and attempts to match the best with the provided `Accept` header values using
// functions from `FSharpx.Http`.
let runConneg formatters (f: HttpRequestMessage -> Async<_>) =
  let bestOf = negotiateMediaType formatters
  fun request ->
    match bestOf request with
    | Some mediaType ->
        let formatter = findFormatterFor mediaType formatters
        async {
          let! responseBody = f request
          let formattedBody = responseBody |> formatWith mediaType formatter
          return respond HttpStatusCode.OK <| ``Content-Type`` mediaType *> ``Vary`` "Accept" <| formattedBody }
    | _ -> ``406 Not Acceptable`` request

// ## HTTP Resources

let internal resourceHandlerOrDefault methods handler request =
  match handler request with
  | Some h -> h
  | _ -> ``405 Method Not Allowed`` methods request

// HTTP resources expose an resource handler function at a given uri.
// In the common MVC-style frameworks, this would roughly correspond
// to a `Controller`. Resources should represent a single entity type,
// and it is important to note that a `Foo` is not the same entity
// type as a `Foo list`, which is where most MVC approaches go wrong. 
// The optional `uriMatcher` parameter allows the consumer to provide
// a more advanced uri matching algorithm, such as one using regular
// expressions.
// 
// Additional notes:
//   Should this type subclass HttpServer? If it did it could get
//   it's own configration and have its own route table. I'm not
//   convinced System.Web.Routing is worth it, but it's an option.
type HttpResource(template, methods, handler) =
  inherit System.Web.Http.Routing.HttpRoute(routeTemplate = template,
                                            defaults = null,
                                            constraints = null,
                                            dataTokens = null,
                                            handler = new AsyncHandler(resourceHandlerOrDefault methods handler))
  with
  member x.Name = template

let private makeHandler(httpMethod, handler) =
  function (request: HttpRequestMessage) when request.Method.Method = httpMethod -> Some(handler request)
         | _ -> None

// Helpers to more easily map `HttpApplication` functions to methods to be composed into `HttpResource`s.
let mapResourceHandler(httpMethod, handler) = [httpMethod], makeHandler(httpMethod, handler)
let get handler = mapResourceHandler(HttpMethod.Get.Method, handler)
let post handler = mapResourceHandler(HttpMethod.Post.Method, handler)
let put handler = mapResourceHandler(HttpMethod.Put.Method, handler)
let delete handler = mapResourceHandler(HttpMethod.Delete.Method, handler)

// Helper to more easily access URL params
let getParam (request:HttpRequestMessage) key =
    let routData = request.GetRouteData()
    let values = routData.Values
    if values.ContainsKey(key) then
        Some(values.[key])
    else None

// We can use several methods to merge multiple handlers together into a single resource.
// Our chosen mechanism here is merging functions into a larger function of the same signature.
// This allows us to create resources as follows:
// 
//     let resource = get app1 <|> post app2 <|> put app3 <|> delete app4
//
// The intent here is to build a resource, with at most one handler per HTTP method. This goes
// against a lot of the "RESTful" approaches that just merge a bunch of method handlers at
// different URI addresses.
let orElse left right =
  fst left @ fst right,
  fun request -> Option.orElse (snd left request) (snd right request)
let inline (<|>) left right = orElse left right

let route uri handler =
  HttpResource(uri, fst handler, snd handler)

let routeResource uri handlers =
  route uri <| Seq.reduce orElse handlers

let ``404 Not Found`` (request: HttpRequestMessage) = async {
  return request.CreateResponse(HttpStatusCode.NotFound)
}

let register<'a when 'a :> System.Web.Http.HttpConfiguration> (resources: seq<HttpResource>) (config: 'a) =
  for resource in resources do
    config.Routes.Add(resource.Name, resource)
  config

// TODO: Re-add support to apply middleware across resources.
