// k6 Average Load Tests for ASP.NET Core MeteredMemoryCache Example
// These tests simulate normal usage patterns with moderate load
// Run with: k6 run k6-average-load-tests.js

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
    averageLoad: scenarios.averageLoad,
  },
  thresholds: {
    ...thresholds,
    // Standard thresholds for average load
    http_req_duration: ["p(95)<1000"],
    response_time: ["p(95)<500"],
    error_rate: ["rate<0.05"],
  },
};

export function setup() {
  console.log(
    "🚀 Starting k6 Average Load Tests for ASP.NET Core MeteredMemoryCache",
  );
  console.log(`📍 Base URL: ${config.baseUrl}`);
  console.log("👥 Simulating 10 concurrent users for 5 minutes");

  // Verify application is running
  const healthResponse = getHealth();
  if (healthResponse.status !== 200) {
    throw new Error(
      `Application health check failed: ${healthResponse.status}`,
    );
  }

  console.log("✅ Application is healthy and ready for average load testing");
  return { startTime: new Date().toISOString() };
}

export default function (data) {
  // Simulate realistic user behavior patterns

  // 70% of requests are reads (GET operations)
  const readOperations = Math.random() < 0.7;

  if (readOperations) {
    // Simulate different read patterns
    const readPattern = Math.random();

    if (readPattern < 0.4) {
      // 40% - Single user lookup (most common)
      const userId = getRandomUserId();
      const userResponse = getUser(userId);
      check(userResponse, {
        "user lookup successful": (r) => r.status === 200,
        "user lookup response time acceptable": (r) => r.timings.duration < 500,
      });
    } else if (readPattern < 0.6) {
      // 20% - Single product lookup
      const productId = getRandomProductId();
      const productResponse = getProduct(productId);
      check(productResponse, {
        "product lookup successful": (r) => r.status === 200,
        "product lookup response time acceptable": (r) =>
          r.timings.duration < 500,
      });
    } else if (readPattern < 0.8) {
      // 20% - Product search
      const searchQuery = getRandomSearchQuery();
      const searchResponse = searchProducts(searchQuery, 1, 10);
      check(searchResponse, {
        "product search successful": (r) => r.status === 200,
        "product search response time acceptable": (r) =>
          r.timings.duration < 1000,
      });
    } else {
      // 20% - Category browsing
      const categoryId = getRandomCategoryId();
      const categoryResponse = getProductsByCategory(categoryId);
      check(categoryResponse, {
        "category browse successful": (r) => r.status === 200,
        "category browse response time acceptable": (r) =>
          r.timings.duration < 1000,
      });
    }
  } else {
    // 30% of requests are writes or batch operations
    const writePattern = Math.random();

    if (writePattern < 0.5) {
      // 15% - Batch user lookup
      const batchUserIds = [
        getRandomUserId(),
        getRandomUserId(),
        getRandomUserId(),
      ];
      const batchResponse = getUsers(batchUserIds);
      check(batchResponse, {
        "batch user lookup successful": (r) => r.status === 200,
        "batch user lookup response time acceptable": (r) =>
          r.timings.duration < 1000,
      });
    } else if (writePattern < 0.8) {
      // 15% - User profile update
      const userId = getRandomUserId();
      const updateData = {
        name: `Updated User ${userId} ${Date.now()}`,
        email: `updated${userId}${Date.now()}@example.com`,
      };
      const updateResponse = updateUser(userId, updateData);
      check(updateResponse, {
        "user update successful": (r) => r.status === 204,
        "user update response time acceptable": (r) =>
          r.timings.duration < 1000,
      });
    } else {
      // 10% - Cache operations
      const cacheOperation = Math.random();

      if (cacheOperation < 0.5) {
        // 5% - Cache statistics check
        const cacheStatsResponse = getCacheStats();
        check(cacheStatsResponse, {
          "cache stats successful": (r) => r.status === 200,
          "cache stats response time acceptable": (r) =>
            r.timings.duration < 200,
        });
      } else {
        // 5% - Cache clear operation
        const cacheName = getRandomCacheName();
        const clearResponse = clearCache(cacheName);
        check(clearResponse, {
          "cache clear successful": (r) => r.status === 204,
          "cache clear response time acceptable": (r) =>
            r.timings.duration < 500,
        });
      }
    }
  }

  // Simulate realistic user think time
  // Users typically wait 1-3 seconds between actions
  sleep(Math.random() * 2 + 1);

  // Occasionally test cache behavior (10% of iterations)
  if (Math.random() < 0.1) {
    const userId = getRandomUserId();
    const cacheTestResult = testCacheHit(userId);
    check(cacheTestResult, {
      "cache hit test successful": (r) =>
        r.miss.status === 200 && r.hit.status === 200,
      "cache hit is faster than miss": (r) => r.cacheHitFaster,
    });

    sleep(0.5);
  }

  // Occasionally test cache invalidation (5% of iterations)
  if (Math.random() < 0.05) {
    const userId = getRandomUserId();
    const invalidationResult = testCacheInvalidation(userId);
    check(invalidationResult, {
      "cache invalidation test successful": (r) =>
        r.before.status === 200 &&
        r.update.status === 204 &&
        r.after.status === 200,
    });

    sleep(0.5);
  }

  // Occasionally check metrics (5% of iterations)
  if (Math.random() < 0.05) {
    const metricsResponse = getMetrics();
    check(metricsResponse, {
      "metrics check successful": (r) => r.status === 200,
      "metrics response time acceptable": (r) => r.timings.duration < 500,
    });
  }
}

export function teardown(data) {
  console.log("🏁 Average load tests completed");
  console.log(`⏱️ Test duration: ${new Date().toISOString()}`);

  // Final health check
  const finalHealthResponse = getHealth();
  if (finalHealthResponse.status === 200) {
    console.log("✅ Application is still healthy after average load tests");
  } else {
    console.log("❌ Application health check failed after average load tests");
  }

  // Final cache statistics
  const finalCacheStats = getCacheStats();
  if (finalCacheStats.status === 200) {
    console.log("📊 Final cache statistics retrieved successfully");
  }
}
