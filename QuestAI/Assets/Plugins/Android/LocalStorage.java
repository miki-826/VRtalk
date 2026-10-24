package com.vibecodingclub.questai;

import android.content.Context;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import org.json.JSONArray;
import org.json.JSONObject;
import java.security.KeyStore;
import javax.crypto.KeyGenerator;
import javax.crypto.Cipher;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** All data is app-private. Only encrypted secret values enter SharedPreferences. */
public final class LocalStorage extends SQLiteOpenHelper {
    private final Context context;
    private static final String ALIAS="QuestAI.BYOK.v1";
    public LocalStorage(Context context) { super(context,"character.db",null,1); this.context=context.getApplicationContext(); setWriteAheadLoggingEnabled(true); }
    @Override public void onConfigure(SQLiteDatabase db) { db.setForeignKeyConstraintsEnabled(true); }
    @Override public void onCreate(SQLiteDatabase db) {
        db.execSQL("CREATE TABLE messages(id TEXT PRIMARY KEY, character_id TEXT NOT NULL, session_id TEXT NOT NULL, created_at INTEGER NOT NULL, body TEXT NOT NULL)");
        db.execSQL("CREATE INDEX messages_character ON messages(character_id,created_at)");
        db.execSQL("CREATE TABLE memories(id TEXT PRIMARY KEY, character_id TEXT NOT NULL, content TEXT NOT NULL, source_message_id TEXT NOT NULL, created_at INTEGER, last_used_at INTEGER, body TEXT NOT NULL, UNIQUE(character_id,content), FOREIGN KEY(source_message_id) REFERENCES messages(id) ON DELETE CASCADE)");
        db.execSQL("CREATE INDEX memories_character ON memories(character_id)");
        db.execSQL("CREATE TABLE settings(id TEXT PRIMARY KEY, body TEXT NOT NULL)");
        for(String name:new String[]{"characters","conversations","relationships","character_states"}) db.execSQL("CREATE TABLE "+name+"(id TEXT PRIMARY KEY, body TEXT NOT NULL)");
    }
    @Override public void onUpgrade(SQLiteDatabase db,int oldVersion,int newVersion) { throw new IllegalStateException("Unsupported database migration"); }
    public synchronized void addMessage(String json) throws Exception {
        JSONObject m=new JSONObject(json); ContentValues v=new ContentValues();
        v.put("id",m.getString("id"));v.put("character_id",m.getString("characterId"));v.put("session_id",m.getString("sessionId"));v.put("created_at",m.getLong("createdAt"));v.put("body",json);
        getWritableDatabase().insertWithOnConflict("messages",null,v,SQLiteDatabase.CONFLICT_IGNORE);
    }
    public synchronized String recent(String id,int limit) throws Exception {
        JSONArray reverse=new JSONArray(), output=new JSONArray();
        try(Cursor c=getReadableDatabase().rawQuery("SELECT body FROM messages WHERE character_id=? ORDER BY created_at DESC,rowid DESC LIMIT ?",new String[]{id,Integer.toString(Math.max(1,Math.min(30,limit)))})) { while(c.moveToNext()) reverse.put(new JSONObject(c.getString(0))); }
        for(int i=reverse.length()-1;i>=0;i--) output.put(reverse.get(i));return output.toString();
    }
    public synchronized String memories(String id) throws Exception {
        JSONArray result=new JSONArray();
        try(Cursor c=getReadableDatabase().rawQuery("SELECT body,last_used_at FROM memories WHERE character_id=? ORDER BY created_at DESC LIMIT 3000",new String[]{id})) { while(c.moveToNext()) {JSONObject m=new JSONObject(c.getString(0));m.put("lastUsedAt",c.getLong(1));result.put(m);} }
        return result.toString();
    }
    public synchronized void touchMemory(String id,long time) { ContentValues v=new ContentValues();v.put("last_used_at",time);getWritableDatabase().update("memories",v,"id=?",new String[]{id}); }
    public synchronized void addMemories(String json) throws Exception {
        SQLiteDatabase db=getWritableDatabase();db.beginTransaction();
        try { JSONArray all=new JSONArray(json);for(int i=0;i<all.length();i++) { JSONObject m=all.getJSONObject(i);ContentValues v=new ContentValues();
            v.put("id",m.getString("id"));v.put("character_id",m.getString("characterId"));v.put("content",m.getString("content"));v.put("source_message_id",m.getString("sourceMessageId"));v.put("created_at",m.getLong("createdAt"));v.put("last_used_at",m.optLong("lastUsedAt"));v.put("body",m.toString());db.insertWithOnConflict("memories",null,v,SQLiteDatabase.CONFLICT_IGNORE);
        } db.setTransactionSuccessful(); } finally { db.endTransaction(); }
    }
    private String tableFor(String key) { if(key.startsWith("state:"))return "character_states";if(key.startsWith("relationship:"))return "relationships";if(key.startsWith("character:"))return "characters";if(key.startsWith("session:"))return "conversations";return "settings"; }
    public synchronized void save(String key,String json) { ContentValues v=new ContentValues();v.put("id",key);v.put("body",json);getWritableDatabase().insertWithOnConflict(tableFor(key),null,v,SQLiteDatabase.CONFLICT_REPLACE); }
    public synchronized String load(String key) { try(Cursor c=getReadableDatabase().rawQuery("SELECT body FROM "+tableFor(key)+" WHERE id=?",new String[]{key})){return c.moveToFirst()?c.getString(0):null;} }
    public synchronized void deleteMemories(String id) { SQLiteDatabase db=getWritableDatabase();db.beginTransaction();try { db.delete("memories","character_id=?",new String[]{id});db.delete("messages","character_id=?",new String[]{id});db.setTransactionSuccessful(); }finally{db.endTransaction();} }
    public synchronized void deleteAllData() throws Exception { SQLiteDatabase db=getWritableDatabase();db.beginTransaction();try { for(String t:new String[]{"memories","messages","settings","characters","conversations","relationships","character_states"})db.delete(t,null,null);db.setTransactionSuccessful();}finally{db.endTransaction();}try(Cursor checkpoint=db.rawQuery("PRAGMA wal_checkpoint(TRUNCATE)",null)){checkpoint.moveToFirst();}db.execSQL("VACUUM");deleteSecrets(); }
    private SecretKey key() throws Exception {
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        if(!store.containsAlias(ALIAS)){KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());generator.generateKey();}
        return ((KeyStore.SecretKeyEntry)store.getEntry(ALIAS,null)).getSecretKey();
    }
    public synchronized void setSecret(String name,String value) throws Exception {
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());cipher.updateAAD(name.getBytes("UTF-8"));
        String encrypted=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)+":"+Base64.encodeToString(cipher.doFinal(value.getBytes("UTF-8")),Base64.NO_WRAP);
        if(!context.getSharedPreferences("encrypted_credentials",Context.MODE_PRIVATE).edit().putString(name,encrypted).commit())throw new IllegalStateException("Credential write failed");
    }
    public synchronized String getSecret(String name) throws Exception {
        String value=context.getSharedPreferences("encrypted_credentials",Context.MODE_PRIVATE).getString(name,"");if(value.isEmpty())return "";
        String[] parts=value.split(":");Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(parts[0],Base64.NO_WRAP)));cipher.updateAAD(name.getBytes("UTF-8"));return new String(cipher.doFinal(Base64.decode(parts[1],Base64.NO_WRAP)),"UTF-8");
    }
    public synchronized void deleteSecrets() throws Exception { if(!context.getSharedPreferences("encrypted_credentials",Context.MODE_PRIVATE).edit().clear().commit())throw new IllegalStateException("Credential deletion failed");KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);if(store.containsAlias(ALIAS))store.deleteEntry(ALIAS); }
}
