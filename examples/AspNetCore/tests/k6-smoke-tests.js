// k6 Smoke Tests for ASP.NET Core MeteredMemoryCache Example
// These tests verify basic functionality and ensure the application is working correctly
// Run with: k6 run k6-smoke-tests.js

import { check, sleep } from "k6";
import {
  config,
  httpOptions,
  thresholds,
  scenarios,
  getHealth,
  getMetrics,
  getUser,
  getProduct,
  getProductsByCategory,
  searchProducts,
  getUsers,
  updateUser,
  getCacheStats,
  clearCache,
  testCacheHit,
  testCacheInvalidation,
  getRandomUserId,
  getRandomProductId,
  getRandomCategoryId,
  getRandomSearchQuery,
  getRandomCacheName,
} from "./k6-config.js";

export let options = {
  scenarios: {
    smoke: scenarios.smoke,
  },
  thresholds: {
    ...thresholds,
    // More lenient thresholds for smoke tests
    http_req_duration: ["p(95)<2000"],
    response_time: ["p(95)<1000"],
    error_rate: ["rate<0.1"],
  },
};

export function setup() {
  console.log("🚀 Starting k6 Smoke Tests for ASP.NET Core MeteredMemoryCache");
  console.log(`📍 Base URL: ${config.baseUrl}`);

  // Verify application is running
  const healthResponse = getHealth();
  if (healthResponse.status !== 200) {
    throw new Error(
      `Application health check failed: ${healthResponse.status}`,
    );
  }

  console.log("✅ Application is healthy and ready for testing");
  return { startTime: new Date().toISOString() };
}

export default function (data) {
  console.log(`🧪 Running smoke test iteration at ${new Date().toISOString()}`);

  // Test 1: Health Check
  console.log("🔍 Testing health endpoint...");
  const healthResponse = getHealth();
  check(healthResponse, {
    "health endpoint is accessible": (r) => r.status === 200,
    "health response time is acceptable": (r) => r.timings.duration < 200,
  });

  // Test 2: Metrics Endpoint
  console.log("📊 Testing metrics endpoint...");
  const metricsResponse = getMetrics();
  check(metricsResponse, {
    "metrics endpoint is accessible": (r) => r.status === 200,
    "metrics response contains cache metrics": (r) =>
      r.body.includes("cache_hits_total") ||
      r.body.includes("cache_misses_total"),
  });

  // Test 3: Cache Statistics
  console.log("📈 Testing cache statistics...");
  const cacheStatsResponse = getCacheStats();
  check(cacheStatsResponse, {
    "cache stats endpoint is accessible": (r) => r.status === 200,
    "cache stats response is valid JSON": (r) => r.json() !== null,
    "cache stats contains cache information": (r) =>
      r.json("caches") !== undefined,
  });

  sleep(0.5);

  // Test 4: User API - Single User
  console.log("👤 Testing user API...");
  const userId = getRandomUserId();
  const userResponse = getUser(userId);
  check(userResponse, {
    "user API is accessible": (r) => r.status === 200,
    "user response contains valid data": (r) => r.json("id") === userId,
    "user response time is acceptable": (r) => r.timings.duration < 1000,
  });

  // Test 5: Product API - Single Product
  console.log("🛍️ Testing product API...");
  const productId = getRandomProductId();
  const productResponse = getProduct(productId);
  check(productResponse, {
    "product API is accessible": (r) => r.status === 200,
    "product response contains valid data": (r) => r.json("id") === productId,
    "product response time is acceptable": (r) => r.timings.duration < 1000,
  });

  // Test 6: Product API - Category
  console.log("📂 Testing product category API...");
  const categoryId = getRandomCategoryId();
  const categoryResponse = getProductsByCategory(categoryId);
  check(categoryResponse, {
    "category API is accessible": (r) => r.status === 200,
    "category response is array": (r) => Array.isArray(r.json()),
    "category response time is acceptable": (r) => r.timings.duration < 1500,
  });

  // Test 7: Product API - Search
  console.log("🔍 Testing product search API...");
  const searchQuery = getRandomSearchQuery();
  const searchResponse = searchProducts(searchQuery, 1, 10);
  check(searchResponse, {
    "search API is accessible": (r) => r.status === 200,
    "search response is array": (r) => Array.isArray(r.json()),
    "search response time is acceptable": (r) => r.timings.duration < 1500,
  });

  sleep(0.5);

  // Test 8: Batch User API
  console.log("👥 Testing batch user API...");
  const batchUserIds = [1, 2, 3];
  const batchUserResponse = getUsers(batchUserIds);
  check(batchUserResponse, {
    "batch user API is accessible": (r) => r.status === 200,
    "batch user response is array": (r) => Array.isArray(r.json()),
    "batch user response time is acceptable": (r) => r.timings.duration < 1500,
  });

  // Test 9: User Update API
  console.log("✏️ Testing user update API...");
  const updateUserId = getRandomUserId();
  const updateData = {
    name: `Smoke Test User ${updateUserId}`,
    email: `smoketest${updateUserId}@example.com`,
  };
  const updateResponse = updateUser(updateUserId, updateData);
  check(updateResponse, {
    "user update API is accessible": (r) => r.status === 204,
    "user update response time is acceptable": (r) => r.timings.duration < 1000,
  });

  sleep(0.5);

  // Test 10: Cache Hit/Miss Behavior
  console.log("💾 Testing cache hit/miss behavior...");
  const cacheTestUserId = getRandomUserId();
  const cacheTestResult = testCacheHit(cacheTestUserId);
  check(cacheTestResult, {
    "cache miss request successful": (r) => r.miss.status === 200,
    "cache hit request successful": (r) => r.hit.status === 200,
    "cache hit is faster than miss": (r) => r.cacheHitFaster,
  });

  // Test 11: Cache Invalidation
  console.log("🗑️ Testing cache invalidation...");
  const invalidationUserId = getRandomUserId();
  const invalidationResult = testCacheInvalidation(invalidationUserId);
  check(invalidationResult, {
    "cache invalidation before request successful": (r) =>
      r.before.status === 200,
    "cache invalidation update request successful": (r) =>
      r.update.status === 204,
    "cache invalidation after request successful": (r) =>
      r.after.status === 200,
  });

  // Test 12: Cache Management
  console.log("🧹 Testing cache management...");
  const cacheName = getRandomCacheName();
  const clearResponse = clearCache(cacheName);
  check(clearResponse, {
    "cache clear API is accessible": (r) => r.status === 204,
    "cache clear response time is acceptable": (r) => r.timings.duration < 500,
  });

  sleep(0.5);

  // Test 13: Error Handling - Non-existent User
  console.log("❌ Testing error handling...");
  const nonExistentUserResponse = getUser(99999);
  check(nonExistentUserResponse, {
    "non-existent user returns 404": (r) => r.status === 404,
    "error response time is acceptable": (r) => r.timings.duration < 1000,
  });

  // Test 14: Error Handling - Invalid Search
  console.log("❌ Testing invalid search...");
  const invalidSearchResponse = searchProducts("", 1, 10);
  check(invalidSearchResponse, {
    "invalid search returns 400": (r) => r.status === 400,
    "error response time is acceptable": (r) => r.timings.duration < 1000,
  });

  console.log("✅ Smoke test iteration completed successfully");
}

export function teardown(data) {
  console.log("🏁 Smoke tests completed");
  console.log(`⏱️ Test duration: ${new Date().toISOString()}`);

  // Final health check
  const finalHealthResponse = getHealth();
  if (finalHealthResponse.status === 200) {
    console.log("✅ Application is still healthy after smoke tests");
  } else {
    console.log("❌ Application health check failed after smoke tests");
  }
}
