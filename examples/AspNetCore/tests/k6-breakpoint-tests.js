// k6 Breakpoint Tests for ASP.NET Core MeteredMemoryCache Example
// These tests gradually increase load to identify capacity limits and breaking points
// Run with: k6 run k6-breakpoint-tests.js

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
    breakpoint: scenarios.breakpoint,
  },
  thresholds: {
    // Gradual threshold relaxation as load increases
    http_req_duration: ["p(95)<2000"],
    http_req_failed: ["rate<0.25"], // Allow up to 25% failure rate at breaking point
    response_time: ["p(95)<1500"],
    cache_response_time: ["p(95)<300"],
    api_response_time: ["p(95)<2000"],
    error_rate: ["rate<0.2"], // Allow up to 20% error rate at breaking point
    cache_hit_rate: ["rate>0.5"], // Cache hit rate may degrade significantly
    cache_miss_rate: ["rate<0.5"],
  },
};

export function setup() {
  console.log(
    "🚀 Starting k6 Breakpoint Tests for ASP.NET Core MeteredMemoryCache",
  );
  console.log(`📍 Base URL: ${config.baseUrl}`);
  console.log(
    "📈 Gradually increasing load: 10 → 20 → 30 → 40 → 50 VUs over 10 minutes",
  );

  // Verify application is running
  const healthResponse = getHealth();
  if (healthResponse.status !== 200) {
    throw new Error(
      `Application health check failed: ${healthResponse.status}`,
    );
  }

  console.log("✅ Application is healthy and ready for breakpoint testing");
  return { startTime: new Date().toISOString() };
}

export default function (data) {
  // Breakpoint test with gradually increasing load

  // Determine current load phase based on test duration
  const currentTime = new Date().getTime();
  const testStartTime = new Date(data.startTime).getTime();
  const elapsedMinutes = (currentTime - testStartTime) / (1000 * 60);

  let currentLoadLevel = 1; // 1-5 scale
  let thinkTime, operationIntensity, burstProbability;

  if (elapsedMinutes < 2) {
    currentLoadLevel = 1; // 10 VUs
    thinkTime = Math.random() * 2 + 1; // 1-3 seconds
    operationIntensity = 0.3;
    burstProbability = 0.05;
  } else if (elapsedMinutes < 4) {
    currentLoadLevel = 2; // 20 VUs
    thinkTime = Math.random() * 1.5 + 0.5; // 0.5-2 seconds
    operationIntensity = 0.4;
    burstProbability = 0.1;
  } else if (elapsedMinutes < 6) {
    currentLoadLevel = 3; // 30 VUs
    thinkTime = Math.random() * 1 + 0.3; // 0.3-1.3 seconds
    operationIntensity = 0.5;
    burstProbability = 0.15;
  } else if (elapsedMinutes < 8) {
    currentLoadLevel = 4; // 40 VUs
    thinkTime = Math.random() * 0.8 + 0.2; // 0.2-1 seconds
    operationIntensity = 0.6;
    burstProbability = 0.2;
  } else {
    currentLoadLevel = 5; // 50 VUs
    thinkTime = Math.random() * 0.5 + 0.1; // 0.1-0.6 seconds
    operationIntensity = 0.7;
    burstProbability = 0.25;
  }

  // Perform operations based on current load level
  if (Math.random() < operationIntensity) {
    const operationType = Math.random();

    if (operationType < 0.3) {
      // 30% - User operations
      const userId = getRandomUserId();
      const userResponse = getUser(userId);
      check(userResponse, {
        "breakpoint user lookup": (r) => r.status === 200,
        "breakpoint user response time": (r) => r.timings.duration < 2000,
      });

      // Test cache behavior at higher load levels
      if (currentLoadLevel >= 3 && Math.random() < 0.4) {
        sleep(0.05);
        const userResponse2 = getUser(userId);
        check(userResponse2, {
          "breakpoint user cache hit": (r) => r.status === 200,
          "breakpoint user cache response time": (r) =>
            r.timings.duration < 1000,
        });
      }
    } else if (operationType < 0.5) {
      // 20% - Product operations
      const productId = getRandomProductId();
      const productResponse = getProduct(productId);
      check(productResponse, {
        "breakpoint product lookup": (r) => r.status === 200,
        "breakpoint product response time": (r) => r.timings.duration < 2000,
      });

      // Test cache behavior at higher load levels
      if (currentLoadLevel >= 3 && Math.random() < 0.4) {
        sleep(0.05);
        const productResponse2 = getProduct(productId);
        check(productResponse2, {
          "breakpoint product cache hit": (r) => r.status === 200,
          "breakpoint product cache response time": (r) =>
            r.timings.duration < 1000,
        });
      }
    } else if (operationType < 0.7) {
      // 20% - Search operations
      const searchQuery = getRandomSearchQuery();
      const searchResponse = searchProducts(searchQuery, 1, 10);
      check(searchResponse, {
        "breakpoint search operation": (r) => r.status === 200,
        "breakpoint search response time": (r) => r.timings.duration < 2000,
      });
    } else if (operationType < 0.85) {
      // 15% - Batch operations
      const batchUserIds = [
        getRandomUserId(),
        getRandomUserId(),
        getRandomUserId(),
      ];
      const batchResponse = getUsers(batchUserIds);
      check(batchResponse, {
        "breakpoint batch user lookup": (r) => r.status === 200,
        "breakpoint batch response time": (r) => r.timings.duration < 2000,
      });
    } else if (operationType < 0.95) {
      // 10% - Update operations
      const userId = getRandomUserId();
      const updateData = {
        name: `Breakpoint Test User ${userId} ${Date.now()}`,
        email: `breakpoint${userId}${Date.now()}@example.com`,
      };
      const updateResponse = updateUser(userId, updateData);
      check(updateResponse, {
        "breakpoint user update": (r) => r.status === 204,
        "breakpoint update response time": (r) => r.timings.duration < 2000,
      });
    } else {
      // 5% - Cache management operations
      const cacheOperation = Math.random();

      if (cacheOperation < 0.5) {
        const cacheStatsResponse = getCacheStats();
        check(cacheStatsResponse, {
          "breakpoint cache stats": (r) => r.status === 200,
          "breakpoint cache stats response time": (r) =>
            r.timings.duration < 1500,
        });
      } else {
        const cacheName = getRandomCacheName();
        const clearResponse = clearCache(cacheName);
        check(clearResponse, {
          "breakpoint cache clear": (r) => r.status === 204,
          "breakpoint cache clear response time": (r) =>
            r.timings.duration < 1500,
        });
      }
    }
  }

  // Burst operations increase with load level
  if (Math.random() < burstProbability) {
    const burstOperations = Math.floor(Math.random() * 5) + 2; // 2-6 operations

    for (let i = 0; i < burstOperations; i++) {
      const userId = getRandomUserId();
      const userResponse = getUser(userId);
      check(userResponse, {
        "breakpoint burst user request": (r) => r.status === 200,
      });
      sleep(0.01); // Very short delay between burst requests
    }
  }

  // Cache testing increases with load level
  if (currentLoadLevel >= 2 && Math.random() < 0.1) {
    const userId = getRandomUserId();
    const cacheTestResult = testCacheHit(userId);
    check(cacheTestResult, {
      "breakpoint cache hit test": (r) =>
        r.miss.status === 200 && r.hit.status === 200,
      "breakpoint cache performance": (r) => r.cacheHitFaster,
    });
  }

  // Cache invalidation testing at higher load levels
  if (currentLoadLevel >= 3 && Math.random() < 0.05) {
    const userId = getRandomUserId();
    const invalidationResult = testCacheInvalidation(userId);
    check(invalidationResult, {
      "breakpoint cache invalidation": (r) =>
        r.before.status === 200 &&
        r.update.status === 204 &&
        r.after.status === 200,
    });
  }

  // Health monitoring increases with load level
  if (currentLoadLevel >= 2 && Math.random() < 0.05) {
    const healthResponse = getHealth();
    check(healthResponse, {
      "breakpoint health check": (r) => r.status === 200,
      "breakpoint health response time": (r) => r.timings.duration < 1000,
    });
  }

  // Metrics monitoring increases with load level
  if (currentLoadLevel >= 3 && Math.random() < 0.03) {
    const metricsResponse = getMetrics();
    check(metricsResponse, {
      "breakpoint metrics check": (r) => r.status === 200,
      "breakpoint metrics response time": (r) => r.timings.duration < 1500,
    });
  }

  // Think time decreases with load level
  sleep(thinkTime);

  // Simulate user session patterns at different load levels
  if (Math.random() < 0.1) {
    const sessionUserId = getRandomUserId();

    // User session: profile → search → product view
    const userResponse = getUser(sessionUserId);
    check(userResponse, {
      "breakpoint session user lookup": (r) => r.status === 200,
    });

    sleep(currentLoadLevel >= 4 ? 0.1 : 0.5);

    const searchQuery = getRandomSearchQuery();
    const searchResponse = searchProducts(searchQuery, 1, 10);
    check(searchResponse, {
      "breakpoint session search": (r) => r.status === 200,
    });

    sleep(currentLoadLevel >= 4 ? 0.1 : 1);

    const productId = getRandomProductId();
    const productResponse = getProduct(productId);
    check(productResponse, {
      "breakpoint session product view": (r) => r.status === 200,
    });
  }

  // Cache pressure testing at higher load levels
  if (currentLoadLevel >= 4 && Math.random() < 0.1) {
    // Generate cache pressure by accessing many different items
    for (let i = 0; i < 15; i++) {
      const userId = getRandomUserId();
      const userResponse = getUser(userId);
      check(userResponse, {
        "breakpoint cache pressure user": (r) => r.status === 200,
      });
      sleep(0.05);
    }
  }

  // System stress testing at highest load level
  if (currentLoadLevel >= 5 && Math.random() < 0.05) {
    // Perform multiple operations rapidly
    const operations = [
      () => getUser(getRandomUserId()),
      () => getProduct(getRandomProductId()),
      () => searchProducts(getRandomSearchQuery(), 1, 10),
      () => getUsers([getRandomUserId(), getRandomUserId()]),
      () => getCacheStats(),
    ];

    for (let i = 0; i < 3; i++) {
      const operation =
        operations[Math.floor(Math.random() * operations.length)];
      const response = operation();
      check(response, {
        "breakpoint system stress operation": (r) =>
          r.status === 200 || r.status === 204,
      });
      sleep(0.01);
    }
  }
}

export function teardown(data) {
  console.log("🏁 Breakpoint tests completed");
  console.log(`⏱️ Test duration: ${new Date().toISOString()}`);

  // Final health check
  const finalHealthResponse = getHealth();
  if (finalHealthResponse.status === 200) {
    console.log("✅ Application is still healthy after breakpoint tests");
  } else {
    console.log("❌ Application health check failed after breakpoint tests");
  }

  // Final cache statistics
  const finalCacheStats = getCacheStats();
  if (finalCacheStats.status === 200) {
    console.log("📊 Final cache statistics retrieved successfully");
  }

  // Final metrics check
  const finalMetrics = getMetrics();
  if (finalMetrics.status === 200) {
    console.log("📈 Final metrics retrieved successfully");
  }

  console.log("🔍 Breakpoint test analysis recommendations:");
  console.log(
    "   - Identify the load level where performance degrades significantly",
  );
  console.log("   - Check response time patterns at different load levels");
  console.log("   - Monitor error rates as load increases");
  console.log("   - Verify cache performance under increasing load");
  console.log("   - Determine the maximum sustainable load");
  console.log("   - Plan capacity scaling based on breaking points");
}
