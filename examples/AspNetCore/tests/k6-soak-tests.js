// k6 Soak Tests for ASP.NET Core MeteredMemoryCache Example
// These tests run for extended periods to identify memory leaks and stability issues
// Run with: k6 run k6-soak-tests.js

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
    soak: scenarios.soak,
  },
  thresholds: {
    // Strict thresholds for soak tests - system should remain stable
    http_req_duration: ["p(95)<1000"],
    http_req_failed: ["rate<0.01"], // Very low error rate expected
    response_time: ["p(95)<500"],
    cache_response_time: ["p(95)<100"],
    api_response_time: ["p(95)<1000"],
    error_rate: ["rate<0.01"], // Very low error rate expected
    cache_hit_rate: ["rate>0.8"], // High cache hit rate expected
    cache_miss_rate: ["rate<0.2"],
  },
};

export function setup() {
  console.log("🚀 Starting k6 Soak Tests for ASP.NET Core MeteredMemoryCache");
  console.log(`📍 Base URL: ${config.baseUrl}`);
  console.log("⏰ Running 5 concurrent users for 30 minutes to test stability");

  // Verify application is running
  const healthResponse = getHealth();
  if (healthResponse.status !== 200) {
    throw new Error(
      `Application health check failed: ${healthResponse.status}`,
    );
  }

  // Get initial cache statistics for comparison
  const initialCacheStats = getCacheStats();
  if (initialCacheStats.status === 200) {
    console.log("📊 Initial cache statistics captured");
  }

  console.log("✅ Application is healthy and ready for soak testing");
  return {
    startTime: new Date().toISOString(),
    initialCacheStats: initialCacheStats.json(),
  };
}

export default function (data) {
  // Soak test with sustained, realistic load patterns

  // Realistic user think time for sustained testing
  const thinkTime = Math.random() * 3 + 1; // 1-4 seconds

  // Mix of operations to simulate real user behavior over time
  const operationType = Math.random();

  if (operationType < 0.4) {
    // 40% - User profile operations (most common in real usage)
    const userId = getRandomUserId();
    const userResponse = getUser(userId);
    check(userResponse, {
      "soak user lookup": (r) => r.status === 200,
      "soak user response time": (r) => r.timings.duration < 500,
    });
  } else if (operationType < 0.6) {
    // 20% - Product browsing
    const productId = getRandomProductId();
    const productResponse = getProduct(productId);
    check(productResponse, {
      "soak product lookup": (r) => r.status === 200,
      "soak product response time": (r) => r.timings.duration < 500,
    });
  } else if (operationType < 0.75) {
    // 15% - Product search
    const searchQuery = getRandomSearchQuery();
    const searchResponse = searchProducts(searchQuery, 1, 10);
    check(searchResponse, {
      "soak product search": (r) => r.status === 200,
      "soak search response time": (r) => r.timings.duration < 1000,
    });
  } else if (operationType < 0.85) {
    // 10% - Category browsing
    const categoryId = getRandomCategoryId();
    const categoryResponse = getProductsByCategory(categoryId);
    check(categoryResponse, {
      "soak category browse": (r) => r.status === 200,
      "soak category response time": (r) => r.timings.duration < 1000,
    });
  } else if (operationType < 0.95) {
    // 10% - Batch operations
    const batchUserIds = [
      getRandomUserId(),
      getRandomUserId(),
      getRandomUserId(),
    ];
    const batchResponse = getUsers(batchUserIds);
    check(batchResponse, {
      "soak batch user lookup": (r) => r.status === 200,
      "soak batch response time": (r) => r.timings.duration < 1000,
    });
  } else {
    // 5% - Update operations
    const userId = getRandomUserId();
    const updateData = {
      name: `Soak Test User ${userId} ${Date.now()}`,
      email: `soak${userId}${Date.now()}@example.com`,
    };
    const updateResponse = updateUser(userId, updateData);
    check(updateResponse, {
      "soak user update": (r) => r.status === 204,
      "soak update response time": (r) => r.timings.duration < 1000,
    });
  }

  // Periodic cache behavior testing (every 10th iteration)
  if (Math.random() < 0.1) {
    const userId = getRandomUserId();
    const cacheTestResult = testCacheHit(userId);
    check(cacheTestResult, {
      "soak cache hit test": (r) =>
        r.miss.status === 200 && r.hit.status === 200,
      "soak cache performance": (r) => r.cacheHitFaster,
    });
  }

  // Periodic cache invalidation testing (every 20th iteration)
  if (Math.random() < 0.05) {
    const userId = getRandomUserId();
    const invalidationResult = testCacheInvalidation(userId);
    check(invalidationResult, {
      "soak cache invalidation": (r) =>
        r.before.status === 200 &&
        r.update.status === 204 &&
        r.after.status === 200,
    });
  }

  // Periodic cache statistics monitoring (every 50th iteration)
  if (Math.random() < 0.02) {
    const cacheStatsResponse = getCacheStats();
    check(cacheStatsResponse, {
      "soak cache stats": (r) => r.status === 200,
      "soak cache stats response time": (r) => r.timings.duration < 200,
    });
  }

  // Periodic metrics monitoring (every 100th iteration)
  if (Math.random() < 0.01) {
    const metricsResponse = getMetrics();
    check(metricsResponse, {
      "soak metrics check": (r) => r.status === 200,
      "soak metrics response time": (r) => r.timings.duration < 500,
    });
  }

  // Periodic health checks (every 200th iteration)
  if (Math.random() < 0.005) {
    const healthResponse = getHealth();
    check(healthResponse, {
      "soak health check": (r) => r.status === 200,
      "soak health response time": (r) => r.timings.duration < 100,
    });
  }

  // Occasional cache management operations (every 1000th iteration)
  if (Math.random() < 0.001) {
    const cacheName = getRandomCacheName();
    const clearResponse = clearCache(cacheName);
    check(clearResponse, {
      "soak cache clear": (r) => r.status === 204,
      "soak cache clear response time": (r) => r.timings.duration < 500,
    });
  }

  // Realistic user think time
  sleep(thinkTime);

  // Simulate user session patterns
  // Occasionally perform a series of related operations (10% of iterations)
  if (Math.random() < 0.1) {
    const sessionUserId = getRandomUserId();

    // User browses their profile
    const userResponse = getUser(sessionUserId);
    check(userResponse, {
      "soak session user lookup": (r) => r.status === 200,
    });

    sleep(0.5);

    // User searches for products
    const searchQuery = getRandomSearchQuery();
    const searchResponse = searchProducts(searchQuery, 1, 10);
    check(searchResponse, {
      "soak session search": (r) => r.status === 200,
    });

    sleep(1);

    // User views a product
    const productId = getRandomProductId();
    const productResponse = getProduct(productId);
    check(productResponse, {
      "soak session product view": (r) => r.status === 200,
    });

    sleep(2);

    // User updates their profile
    const updateData = {
      name: `Session User ${sessionUserId} ${Date.now()}`,
      email: `session${sessionUserId}${Date.now()}@example.com`,
    };
    const updateResponse = updateUser(sessionUserId, updateData);
    check(updateResponse, {
      "soak session user update": (r) => r.status === 204,
    });
  }

  // Simulate cache pressure over time (5% of iterations)
  if (Math.random() < 0.05) {
    // Generate cache pressure by accessing many different items
    for (let i = 0; i < 10; i++) {
      const userId = getRandomUserId();
      const userResponse = getUser(userId);
      check(userResponse, {
        "soak cache pressure user": (r) => r.status === 200,
      });
      sleep(0.1);
    }
  }
}

export function teardown(data) {
  console.log("🏁 Soak tests completed");
  console.log(`⏱️ Test duration: ${new Date().toISOString()}`);

  // Final health check
  const finalHealthResponse = getHealth();
  if (finalHealthResponse.status === 200) {
    console.log("✅ Application is still healthy after soak tests");
  } else {
    console.log("❌ Application health check failed after soak tests");
  }

  // Final cache statistics for comparison
  const finalCacheStats = getCacheStats();
  if (finalCacheStats.status === 200) {
    console.log("📊 Final cache statistics retrieved successfully");

    // Compare with initial stats if available
    if (data.initialCacheStats) {
      console.log("📈 Cache statistics comparison:");
      console.log("   Initial stats captured at start of test");
      console.log("   Final stats captured at end of test");
      console.log("   Monitor for memory leaks and cache growth patterns");
    }
  }

  // Final metrics check
  const finalMetrics = getMetrics();
  if (finalMetrics.status === 200) {
    console.log("📈 Final metrics retrieved successfully");
    console.log(
      "   Review metrics for memory usage patterns and performance trends",
    );
  }

  console.log("🔍 Soak test analysis recommendations:");
  console.log("   - Check for memory leaks in application logs");
  console.log("   - Monitor cache hit/miss ratios over time");
  console.log("   - Verify response times remain stable");
  console.log("   - Check for any error rate increases");
  console.log("   - Review OpenTelemetry metrics for trends");
}
