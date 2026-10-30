package com.gani.agentbuddy.data.local

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import com.gani.agentbuddy.data.model.RDAccount

@Database(entities = [RDAccount::class], version = 1, exportSchema = false)
abstract class AgentBuddyDatabase : RoomDatabase() {

    abstract fun rdAccountDao(): RDAccountDao

    companion object {
        @Volatile
        private var INSTANCE: AgentBuddyDatabase? = null

        fun getDatabase(context: Context): AgentBuddyDatabase {
            return INSTANCE ?: synchronized(this) {
                val instance = Room.databaseBuilder(
                    context.applicationContext,
                    AgentBuddyDatabase::class.java,
                    "agentbuddy.db"
                ).build()
                INSTANCE = instance
                instance
            }
        }
    }
}
