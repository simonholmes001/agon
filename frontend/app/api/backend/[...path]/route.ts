import type { NextRequest } from "next/server";
import { createLogger } from "@/lib/logger";

const logger = createLogger("BackendProxyRoute");

const DEFAULT_BACKEND_BASE_URL = "http://localhost:5000";
const METHODS_WITHOUT_BODY = new Set(["GET", "HEAD"]);

function getBackendBaseUrl(): string {
  return (process.env.BACKEND_API_BASE_URL ?? DEFAULT_BACKEND_BASE_URL)
    .replace(/\/+$/, "");
}

function buildBackendUrl(request: NextRequest, path: string[]): URL {
  const backendPath = path.join("/");
  const url = new URL(`${getBackendBaseUrl()}/${backendPath}`);

  request.nextUrl.searchParams.forEach((value, key) => {
    url.searchParams.append(key, value);
  });

  return url;
}

function buildForwardHeaders(request: NextRequest): Headers {
  const headers = new Headers(request.headers);
  headers.delete("host");
  headers.delete("connection");
  headers.delete("content-length");
  return headers;
}

async function proxyRequest(
  request: NextRequest,
  path: string[],
): Promise<Response> {
  const targetUrl = buildBackendUrl(request, path);
  const method = request.method.toUpperCase();

  try {
    let body: string | undefined;
    if (!METHODS_WITHOUT_BODY.has(method)) {
      const rawBody = await request.text();
      if (rawBody.length > 0) {
        body = rawBody;
      }
    }

    const upstreamResponse = await fetch(targetUrl, {
      method,
      headers: buildForwardHeaders(request),
      body,
      redirect: "manual",
    });

    return new Response(upstreamResponse.body, {
      status: upstreamResponse.status,
      statusText: upstreamResponse.statusText,
      headers: upstreamResponse.headers,
    });
  } catch (error) {
    logger.error(
      "backend proxy request failed",
      {
        method,
        targetUrl: targetUrl.toString(),
      },
      error,
    );
    return Response.json(
      { error: "Failed to reach backend service." },
      { status: 502 },
    );
  }
}

interface RouteContext {
  params: Promise<{ path: string[] }>;
}

async function handle(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  const { path } = await context.params;
  return proxyRequest(request, path);
}

export async function GET(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  return handle(request, context);
}

export async function POST(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  return handle(request, context);
}

export async function PUT(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  return handle(request, context);
}

export async function PATCH(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  return handle(request, context);
}

export async function DELETE(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  return handle(request, context);
}

export async function OPTIONS(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  return handle(request, context);
}

export async function HEAD(
  request: NextRequest,
  context: RouteContext,
): Promise<Response> {
  return handle(request, context);
}
