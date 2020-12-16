# Valdis

## What is Valdis?

**Valdis** is a .NET API gateway. All API traffic is enter into the system via this gateway. Two operations happen once the traffic enters. Firstly, validation happen. Authentication and authorization are main aspects of validation. Secondly, distribution happens. Each API should be passed through a specific path through a specific target. Having microservice architecture in mind, each target could be imagined as a microservice. Term **Valdis** comes from validation and distribution.

## How it works?

1. Client could request JWT from **Valdis**. In next step, **Valdis** validates the client's request based on its internal settings and user's data. After this, a JWT is issued. Tokens may be black listed or not. Then, client sends JWT along with any request of a protected API.

2. **Valdis** receives API requests. It checks if the request URL is protected or not. If yest, then the token provided by client is checked. If it suffices, then the request is passed to distribution phase. If it does not suffice, then a `401 Unauthorize` error is returned.

3. Based on the URL, **Valdis** dispatch the request to the specific API, waits for the response, then returns back the response to the client. A load balancing is possible here.

## Features

* It is installable as a microservice based on *docker*

## Contributing

Any contribution from the community is appreciated.
