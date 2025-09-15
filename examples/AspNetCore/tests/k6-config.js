// k6 Configuration and Helper Functions
// This file contains shared configuration and utility functions for k6 tests

import { check, sleep } from "k6";
import http from "k6/http";
import { Rate, Trend, Counter } from "k6/metrics";

// Custom metrics for cache performance monitoring
export const cacheHitRate = new Rate("cache_hit_rate");
export const cacheMissRate = new Rate("cache_miss_rate");
export const cacheEvictionRate = new Rate("cache_eviction_rate");
export const responseTime = new Trend("response_time");
export const cacheResponseTime = new Trend("cache_response_time");
export const apiResponseTime = new Trend("api_response_time");
export const errorRate = new Rate("error_rate");
export const requestCount = new Counter("request_count");

// Test configuration
export const config = {
  baseUrl: __ENV.BASE_URL || "https://localhost:64494",
  httpHostUrl: __ENV.HTTP_HOST_URL || "http://localhost:64495",
  testData: {
    userIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
    productIds: [100, 101, 102, 103, 104, 105, 106, 107, 108, 109],
    categoryIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
    searchQueries: [
      "laptop",
      "phone",
      "tablet",
      "monitor",
      "keyboard",
      "mouse",
      "headphones",
      "speaker",
      "camera",
      "printer",
    ],
    cacheNames: [
      "user-profiles",
      "product-catalog",
      "session-data",
      "api-responses",
    ],
  },
};

// HTTP request options
export const httpOptions = {
  headers: {
    Accept: "application/json",
    "Content-Type": "application/json",
    "User-Agent": "k6-load-test/1.0",
  },
  timeout: "30s",
};

// Utility functions
export function getRandomUserId() {
  return config.testData.userIds[
    Math.floor(Math.random() * config.testData.userIds.length)
  ];
}

export function getRandomProductId() {
  return config.testData.productIds[
    Math.floor(Math.random() * config.testData.productIds.length)
  ];
}

export function getRandomCategoryId() {
  return config.testData.categoryIds[
    Math.floor(Math.random() * config.testData.categoryIds.length)
  ];
}

export function getRandomSearchQuery() {
  return config.testData.searchQueries[
    Math.floor(Math.random() * config.testData.searchQueries.length)
  ];
}

export function getRandomCacheName() {
  return config.testData.cacheNames[
    Math.floor(Math.random() * config.testData.cacheNames.length)
  ];
}

// API endpoint functions
export function getUser(userId) {
  const response = http.get(
    `${config.baseUrl}/api/users/${userId}`,
    httpOptions,
  );

  const success = check(response, {
    "user request successful": (r) => r.status === 200,
    "user response time < 500ms": (r) => r.timings.duration < 500,
    "user response has valid JSON": (r) => r.json("id") === userId,
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function getProduct(productId) {
  const response = http.get(
    `${config.baseUrl}/api/products/${productId}`,
    httpOptions,
  );

  const success = check(response, {
    "product request successful": (r) => r.status === 200,
    "product response time < 500ms": (r) => r.timings.duration < 500,
    "product response has valid JSON": (r) => r.json("id") === productId,
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function getProductsByCategory(categoryId) {
  const response = http.get(
    `${config.baseUrl}/api/products/category/${categoryId}`,
    httpOptions,
  );

  const success = check(response, {
    "category request successful": (r) => r.status === 200,
    "category response time < 1000ms": (r) => r.timings.duration < 1000,
    "category response is array": (r) => Array.isArray(r.json()),
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function searchProducts(query, page = 1, pageSize = 10) {
  const response = http.get(
    `${config.baseUrl}/api/products/search?query=${query}&page=${page}&pageSize=${pageSize}`,
    httpOptions,
  );

  const success = check(response, {
    "search request successful": (r) => r.status === 200,
    "search response time < 1000ms": (r) => r.timings.duration < 1000,
    "search response is array": (r) => Array.isArray(r.json()),
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function getUsers(ids) {
  const idsParam = ids.map((id) => `ids=${id}`).join("&");
  const response = http.get(
    `${config.baseUrl}/api/users?${idsParam}`,
    httpOptions,
  );

  const success = check(response, {
    "batch users request successful": (r) => r.status === 200,
    "batch users response time < 1000ms": (r) => r.timings.duration < 1000,
    "batch users response is array": (r) => Array.isArray(r.json()),
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function updateUser(userId, userData) {
  const response = http.put(
    `${config.baseUrl}/api/users/${userId}`,
    JSON.stringify(userData),
    httpOptions,
  );

  const success = check(response, {
    "update user request successful": (r) => r.status === 204,
    "update user response time < 1000ms": (r) => r.timings.duration < 1000,
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function getCacheStats() {
  const response = http.get(`${config.baseUrl}/api/cache/stats`, httpOptions);

  const success = check(response, {
    "cache stats request successful": (r) => r.status === 200,
    "cache stats response time < 200ms": (r) => r.timings.duration < 200,
    "cache stats response has caches": (r) => r.json("caches") !== undefined,
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function clearCache(cacheName) {
  const response = http.del(
    `${config.baseUrl}/api/cache/${cacheName}`,
    null,
    httpOptions,
  );

  const success = check(response, {
    "clear cache request successful": (r) => r.status === 204,
    "clear cache response time < 500ms": (r) => r.timings.duration < 500,
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function getHealth() {
  const response = http.get(`${config.baseUrl}/health`, httpOptions);

  const success = check(response, {
    "health check successful": (r) => r.status === 200,
    "health check response time < 100ms": (r) => r.timings.duration < 100,
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

export function getMetrics() {
  const response = http.get(`${config.baseUrl}/metrics`, {
    headers: { Accept: "text/plain" },
  });

  const success = check(response, {
    "metrics request successful": (r) => r.status === 200,
    "metrics response time < 500ms": (r) => r.timings.duration < 500,
    "metrics response contains cache metrics": (r) =>
      r.body.includes("cache_hits_total") ||
      r.body.includes("cache_misses_total"),
  });

  responseTime.add(response.timings.duration);
  requestCount.add(1);
  errorRate.add(!success);

  return response;
}

// Cache performance testing functions
export function testCacheHit(userId) {
  // First request - should be cache miss
  const response1 = getUser(userId);
  cacheMissRate.add(1);

  // Small delay to ensure cache is populated
  sleep(0.1);

  // Second request - should be cache hit
  const response2 = getUser(userId);
  cacheHitRate.add(1);

  // Verify cache hit is faster
  const cacheHitFaster = check(response2, {
    "cache hit is faster than miss": (r) =>
      r.timings.duration < response1.timings.duration,
  });

  cacheResponseTime.add(response2.timings.duration);
  apiResponseTime.add(response1.timings.duration);

  return { miss: response1, hit: response2, cacheHitFaster };
}

export function testCacheInvalidation(userId) {
  // Get user to populate cache
  const response1 = getUser(userId);
  cacheMissRate.add(1);

  // Update user to invalidate cache
  const updateData = {
    name: `Updated User ${userId}`,
    email: `updated${userId}@example.com`,
  };
  const updateResponse = updateUser(userId, updateData);

  // Small delay to ensure cache invalidation
  sleep(0.1);

  // Get user again - should be cache miss due to invalidation
  const response2 = getUser(userId);
  cacheMissRate.add(1);

  return { before: response1, update: updateResponse, after: response2 };
}

// Performance thresholds
export const thresholds = {
  http_req_duration: ["p(95)<1000"], // 95% of requests must complete below 1s
  http_req_failed: ["rate<0.1"], // Error rate must be below 10%
  response_time: ["p(95)<500"], // 95% of responses must be below 500ms
  cache_response_time: ["p(95)<100"], // 95% of cache hits must be below 100ms
  api_response_time: ["p(95)<1000"], // 95% of API calls must be below 1s
  error_rate: ["rate<0.05"], // Error rate must be below 5%
  cache_hit_rate: ["rate>0.8"], // Cache hit rate should be above 80%
  cache_miss_rate: ["rate<0.2"], // Cache miss rate should be below 20%
};

// Test scenarios
export const scenarios = {
  smoke: {
    executor: "constant-vus",
    vus: 1,
    duration: "1m",
    tags: { test_type: "smoke" },
  },
  averageLoad: {
    executor: "constant-vus",
    vus: 10,
    duration: "5m",
    tags: { test_type: "average_load" },
  },
  stress: {
    executor: "ramping-vus",
    startVUs: 1,
    stages: [
      { duration: "2m", target: 20 },
      { duration: "5m", target: 20 },
      { duration: "2m", target: 0 },
    ],
    tags: { test_type: "stress" },
  },
  soak: {
    executor: "constant-vus",
    vus: 5,
    duration: "30m",
    tags: { test_type: "soak" },
  },
  spike: {
    executor: "ramping-vus",
    startVUs: 1,
    stages: [
      { duration: "1m", target: 10 },
      { duration: "1m", target: 50 },
      { duration: "1m", target: 10 },
      { duration: "1m", target: 0 },
    ],
    tags: { test_type: "spike" },
  },
  breakpoint: {
    executor: "ramping-vus",
    startVUs: 1,
    stages: [
      { duration: "2m", target: 10 },
      { duration: "2m", target: 20 },
      { duration: "2m", target: 30 },
      { duration: "2m", target: 40 },
      { duration: "2m", target: 50 },
      { duration: "2m", target: 0 },
    ],
    tags: { test_type: "breakpoint" },
  },
};
